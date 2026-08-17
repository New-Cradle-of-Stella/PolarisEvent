using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// 严格的 `.pactor` 读取器，工具侧与游戏侧共用，对同一份字节必须得到同一个目录和同一组诊断。
    /// `.pactor` 是纯数据：禁止 DTD、外部实体、未知元素与属性、元素正文，也不接受任意 C# 类型名、方法名或条件。
    /// </summary>
    public static class ActorCatalogReader
    {
        private const string InvalidXml = "PEVT9101";
        private const string UnsupportedVersion = "PEVT9102";
        private const string InvalidNamespace = "PEVT9103";
        private const string ReservedNamespace = "PEVT9104";
        private const string InvalidActorId = "PEVT9105";
        private const string DuplicateActorId = "PEVT9106";
        private const string MissingDisplayName = "PEVT9107";
        private const string InvalidColor = "PEVT9108";
        private const string InvalidProvider = "PEVT9109";
        private const string InvalidResourceReference = "PEVT9110";
        private const string DuplicateVisualId = "PEVT9112";
        private const string MissingDefaultPortrait = "PEVT9113";
        private const string UnknownVisualReference = "PEVT9114";
        private const string ForbiddenLegacyAlias = "PEVT9115";
        private const string InvalidAnchor = "PEVT9116";
        private const string InternalError = "PEVT9001";

        private static readonly XNamespace Ns = ActorCatalog.XmlNamespace;

        private const string ProviderGamePxls = "game-pxls";
        private const string ProviderPolarisRes = "polaris-res";

        /// <summary>严格 UTF-8 解码后读取一个 `.pactor`。<paramref name="sourceKind"/> 由调用方决定，XML 不能自证内置身份。</summary>
        public static ActorCatalogReadResult Read(
            byte[] utf8Bytes,
            string sourcePath,
            ActorCatalogSourceKind sourceKind,
            CancellationToken cancellationToken = default)
        {
            if (utf8Bytes == null)
                throw new ArgumentNullException(nameof(utf8Bytes));

            SourceTextLoadResult loaded = SourceText.FromUtf8(utf8Bytes, sourcePath, cancellationToken);
            if (!loaded.Success)
            {
                var bag = new DiagnosticBag();
                bag.Add(new Diagnostic(InvalidXml, DiagnosticSeverity.Error, "`.pactor` 不是合法的 UTF-8 文本，无法作为 XML 读取。", null));
                return new ActorCatalogReadResult(null, null, bag.ToReadOnly());
            }

            return Read(loaded.Text, sourceKind, cancellationToken);
        }

        /// <summary>测试与编辑器用的便捷入口：直接以 UTF-8 编码给定文本后读取。</summary>
        public static ActorCatalogReadResult ReadText(
            string xml,
            string sourcePath,
            ActorCatalogSourceKind sourceKind,
            CancellationToken cancellationToken = default)
        {
            if (xml == null)
                throw new ArgumentNullException(nameof(xml));
            return Read(new UTF8Encoding(false).GetBytes(xml), sourcePath, sourceKind, cancellationToken);
        }

        public static ActorCatalogReadResult Read(
            SourceText source,
            ActorCatalogSourceKind sourceKind,
            CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var diagnostics = new DiagnosticBag();
            var locations = new SourceLocator(source);

            try
            {
                XDocument document = LoadDocument(source, diagnostics, locations);
                if (document == null)
                    return new ActorCatalogReadResult(null, source, diagnostics.ToReadOnly());

                ActorCatalog catalog = ReadCatalog(document, source, sourceKind, diagnostics, locations, cancellationToken);
                return new ActorCatalogReadResult(
                    diagnostics.HasErrors ? null : catalog,
                    source,
                    diagnostics.ToReadOnly());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    InternalError,
                    DiagnosticSeverity.Error,
                    $"读取 `.pactor` 时发生内部异常：{ex.GetType().Name}: {ex.Message}",
                    locations.WholeFile()));
                return new ActorCatalogReadResult(null, source, diagnostics.ToReadOnly());
            }
        }

        private static XDocument LoadDocument(SourceText source, DiagnosticBag diagnostics, SourceLocator locations)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = false,
                CloseInput = true,
            };

            try
            {
                using (var stringReader = new StringReader(source.Content))
                using (XmlReader reader = XmlReader.Create(stringReader, settings))
                {
                    return XDocument.Load(reader, LoadOptions.SetLineInfo);
                }
            }
            catch (XmlException ex)
            {
                diagnostics.AddError(InvalidXml, $"XML 无效：{ex.Message}", locations.FromLineInfo(ex.LineNumber, ex.LinePosition, 1));
                return null;
            }
        }

        private static ActorCatalog ReadCatalog(
            XDocument document,
            SourceText source,
            ActorCatalogSourceKind sourceKind,
            DiagnosticBag diagnostics,
            SourceLocator locations,
            CancellationToken cancellationToken)
        {
            XElement root = document.Root;
            if (root == null || root.Name != Ns + "ActorCatalog")
            {
                diagnostics.AddError(
                    InvalidXml,
                    $"根元素必须是命名空间 `{ActorCatalog.XmlNamespace}` 下的 `ActorCatalog`。",
                    root != null ? locations.ForElement(root) : locations.WholeFile());
                return null;
            }

            CheckAttributes(root, diagnostics, locations, "Version", "Namespace", "BuiltIn");
            CheckNoTextContent(root, diagnostics, locations);

            int version = ReadVersion(root, diagnostics, locations);
            string catalogNamespace = ReadNamespace(root, diagnostics, locations);
            bool declaredBuiltIn = ReadBuiltIn(root, diagnostics, locations);

            bool trusted = sourceKind == ActorCatalogSourceKind.BuiltIn;
            if (declaredBuiltIn && !trusted)
            {
                diagnostics.AddError(
                    ReservedNamespace,
                    "只有 Polaris 内置目录可以声明 `BuiltIn=\"true\"`。",
                    locations.ForAttribute(root.Attribute("BuiltIn")) ?? locations.ForElement(root));
            }

            if (catalogNamespace != null
                && ActorNaming.IdComparer.Equals(catalogNamespace, ActorNaming.BuiltInNamespace)
                && !trusted)
            {
                diagnostics.AddError(
                    ReservedNamespace,
                    $"`{ActorNaming.BuiltInNamespace}` 是封闭命名空间，外部目录不能注册、覆盖或扩展。",
                    locations.ForAttribute(root.Attribute("Namespace")) ?? locations.ForElement(root));
            }

            bool isBuiltIn = declaredBuiltIn && trusted;

            var actors = new List<ActorDefinition>();
            var seenLocalIds = new HashSet<string>(ActorNaming.IdComparer);

            foreach (XElement child in root.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (child.Name != Ns + "Actor")
                {
                    diagnostics.AddError(InvalidXml, $"`ActorCatalog` 下不允许元素 `{child.Name.LocalName}`。", locations.ForElement(child));
                    continue;
                }

                ActorDefinition actor = ReadActor(child, isBuiltIn, diagnostics, locations);
                if (actor == null)
                    continue;

                if (!seenLocalIds.Add(actor.LocalId))
                {
                    diagnostics.AddError(
                        DuplicateActorId,
                        $"人物局部 ID `{actor.LocalId}` 在同一目录内重复。",
                        locations.ForAttribute(child.Attribute("Id")) ?? locations.ForElement(child));
                    continue;
                }

                actors.Add(actor);
            }

            if (diagnostics.HasErrors || catalogNamespace == null || version != ActorCatalog.SupportedVersion)
                return null;

            return new ActorCatalog(catalogNamespace, version, isBuiltIn, source.FilePath, actors);
        }

        private static int ReadVersion(XElement root, DiagnosticBag diagnostics, SourceLocator locations)
        {
            XAttribute attribute = root.Attribute("Version");
            if (attribute == null)
            {
                diagnostics.AddError(UnsupportedVersion, "`ActorCatalog` 缺少 `Version` 属性。", locations.ForElement(root));
                return -1;
            }

            if (!int.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int version)
                || version != ActorCatalog.SupportedVersion)
            {
                diagnostics.AddError(
                    UnsupportedVersion,
                    $"不支持的 `.pactor` 版本 `{attribute.Value}`；第一版只能为 {ActorCatalog.SupportedVersion}。",
                    locations.ForAttribute(attribute));
                return -1;
            }

            return version;
        }

        private static string ReadNamespace(XElement root, DiagnosticBag diagnostics, SourceLocator locations)
        {
            XAttribute attribute = root.Attribute("Namespace");
            if (attribute == null)
            {
                diagnostics.AddError(InvalidNamespace, "`ActorCatalog` 缺少 `Namespace` 属性。", locations.ForElement(root));
                return null;
            }

            if (!ActorNaming.IsValidNamespace(attribute.Value))
            {
                diagnostics.AddError(
                    InvalidNamespace,
                    $"目录命名空间 `{attribute.Value}` 不合法：只能是以 `.` 连接的小写 ASCII 标识段。",
                    locations.ForAttribute(attribute));
                return null;
            }

            return attribute.Value;
        }

        private static bool ReadBuiltIn(XElement root, DiagnosticBag diagnostics, SourceLocator locations)
        {
            XAttribute attribute = root.Attribute("BuiltIn");
            if (attribute == null)
                return false;

            if (attribute.Value == "true")
                return true;
            if (attribute.Value == "false")
                return false;

            diagnostics.AddError(InvalidXml, $"`BuiltIn` 只能是 `true` 或 `false`，实际为 `{attribute.Value}`。", locations.ForAttribute(attribute));
            return false;
        }

        private static ActorDefinition ReadActor(XElement element, bool isBuiltIn, DiagnosticBag diagnostics, SourceLocator locations)
        {
            CheckAttributes(element, diagnostics, locations,
                "Id", "DisplayName", "DisplayKey", "Voice", "Color", "Icon", "DefaultPortrait", "LegacyPerson");
            CheckNoTextContent(element, diagnostics, locations);

            bool valid = true;

            string localId = element.Attribute("Id")?.Value;
            if (localId == null)
            {
                diagnostics.AddError(InvalidActorId, "`Actor` 缺少 `Id` 属性。", locations.ForElement(element));
                valid = false;
            }
            else if (!ActorNaming.IsValidLocalId(localId))
            {
                diagnostics.AddError(
                    InvalidActorId,
                    $"人物局部 ID `{localId}` 不合法：只能以小写字母开头，后接小写字母、数字、`-` 或 `_`。",
                    locations.ForAttribute(element.Attribute("Id")));
                valid = false;
            }

            string displayName = element.Attribute("DisplayName")?.Value;
            string displayKey = element.Attribute("DisplayKey")?.Value;
            if (string.IsNullOrEmpty(displayName) && string.IsNullOrEmpty(displayKey))
            {
                diagnostics.AddError(MissingDisplayName, "`Actor` 的 `DisplayName` 与 `DisplayKey` 至少要有一个非空值。", locations.ForElement(element));
                valid = false;
            }

            ActorColor? color = null;
            XAttribute colorAttribute = element.Attribute("Color");
            if (colorAttribute != null)
            {
                if (ActorColor.TryParse(colorAttribute.Value, out ActorColor parsed))
                {
                    color = parsed;
                }
                else
                {
                    diagnostics.AddError(
                        InvalidColor,
                        $"颜色 `{colorAttribute.Value}` 不合法：只接受 `#RRGGBB` 或 `#RRGGBBAA`。",
                        locations.ForAttribute(colorAttribute));
                    valid = false;
                }
            }

            ActorVisualResource icon = null;
            XAttribute iconAttribute = element.Attribute("Icon");
            if (iconAttribute != null)
            {
                icon = ReadInheritedResource(iconAttribute, isBuiltIn, diagnostics, locations);
                if (icon == null)
                    valid = false;
            }

            string legacyPerson = ReadLegacyPerson(element, isBuiltIn, diagnostics, locations, ref valid);

            var portraits = new List<ActorVisual>();
            var uiPortraits = new List<ActorVisual>();
            var appearances = new List<ActorAppearance>();
            var anchors = new List<ActorAnchor>();
            ActorVisual worldSprite = null;
            XElement worldSpriteElement = null;

            foreach (XElement child in element.Elements())
            {
                if (child.Name == Ns + "WorldSprite")
                {
                    if (worldSpriteElement != null)
                    {
                        diagnostics.AddError(DuplicateVisualId, "同一人物只能声明一个 `WorldSprite`。", locations.ForElement(child));
                        valid = false;
                        continue;
                    }

                    worldSpriteElement = child;
                    worldSprite = ReadVisual(child, ActorVisualKind.WorldSprite, isBuiltIn, diagnostics, locations);
                    if (worldSprite == null)
                        valid = false;
                }
                else if (child.Name == Ns + "Portrait")
                {
                    ActorVisual portrait = ReadVisual(child, ActorVisualKind.Portrait, isBuiltIn, diagnostics, locations);
                    if (portrait == null)
                        valid = false;
                    else if (!AddUnique(portraits, portrait, visual => visual.Id, "Portrait", child, diagnostics, locations))
                        valid = false;
                }
                else if (child.Name == Ns + "UiPortrait")
                {
                    ActorVisual uiPortrait = ReadVisual(child, ActorVisualKind.UiPortrait, isBuiltIn, diagnostics, locations);
                    if (uiPortrait == null)
                        valid = false;
                    else if (!AddUnique(uiPortraits, uiPortrait, visual => visual.Id, "UiPortrait", child, diagnostics, locations))
                        valid = false;
                }
                else if (child.Name == Ns + "Appearance")
                {
                    ActorAppearance appearance = ReadAppearance(child, diagnostics, locations);
                    if (appearance == null)
                        valid = false;
                    else if (!AddUnique(appearances, appearance, item => item.Id, "Appearance", child, diagnostics, locations))
                        valid = false;
                }
                else if (child.Name == Ns + "Anchor")
                {
                    ActorAnchor anchor = ReadAnchor(child, diagnostics, locations);
                    if (anchor == null)
                        valid = false;
                    else if (!AddUnique(anchors, anchor, item => item.Id, "Anchor", child, diagnostics, locations))
                        valid = false;
                }
                else
                {
                    diagnostics.AddError(InvalidXml, $"`Actor` 下不允许元素 `{child.Name.LocalName}`。", locations.ForElement(child));
                    valid = false;
                }
            }

            string defaultPortraitId = ReadDefaultPortrait(element, portraits, diagnostics, locations, ref valid);
            valid &= ResolveAppearances(appearances, portraits, diagnostics, locations, element);

            if (!valid)
                return null;

            return new ActorDefinition(
                localId,
                displayKey: string.IsNullOrEmpty(displayKey) ? null : displayKey,
                displayName: string.IsNullOrEmpty(displayName) ? null : displayName,
                voice: element.Attribute("Voice")?.Value,
                color: color,
                icon: icon,
                defaultPortraitId: defaultPortraitId,
                legacyPerson: legacyPerson,
                worldSprite: worldSprite,
                portraits: portraits,
                uiPortraits: uiPortraits,
                appearances: appearances,
                anchors: anchors,
                location: locations.ForElement(element));
        }

        private static string ReadDefaultPortrait(
            XElement element,
            List<ActorVisual> portraits,
            DiagnosticBag diagnostics,
            SourceLocator locations,
            ref bool valid)
        {
            XAttribute attribute = element.Attribute("DefaultPortrait");
            if (attribute == null)
            {
                if (portraits.Count > 0)
                {
                    diagnostics.AddError(
                        MissingDefaultPortrait,
                        "人物登记了 `Portrait` 但没有声明 `DefaultPortrait`。",
                        locations.ForElement(element));
                    valid = false;
                }

                return null;
            }

            foreach (ActorVisual portrait in portraits)
            {
                if (ActorNaming.IdComparer.Equals(portrait.Id, attribute.Value))
                    return attribute.Value;
            }

            diagnostics.AddError(
                MissingDefaultPortrait,
                $"`DefaultPortrait=\"{attribute.Value}\"` 没有对应的 `Portrait`。",
                locations.ForAttribute(attribute));
            valid = false;
            return null;
        }

        private static bool ResolveAppearances(
            List<ActorAppearance> appearances,
            List<ActorVisual> portraits,
            DiagnosticBag diagnostics,
            SourceLocator locations,
            XElement actorElement)
        {
            bool valid = true;

            for (int i = appearances.Count - 1; i >= 0; i--)
            {
                ActorAppearance appearance = appearances[i];
                bool found = false;
                foreach (ActorVisual portrait in portraits)
                {
                    if (ActorNaming.IdComparer.Equals(portrait.Id, appearance.PortraitId))
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    continue;

                diagnostics.AddError(
                    UnknownVisualReference,
                    $"appearance `{appearance.Id}` 引用了不存在的 portrait `{appearance.PortraitId}`。",
                    appearance.Location ?? locations.ForElement(actorElement));
                appearances.RemoveAt(i);
                valid = false;
            }

            return valid;
        }

        private static ActorVisual ReadVisual(
            XElement element,
            ActorVisualKind kind,
            bool isBuiltIn,
            DiagnosticBag diagnostics,
            SourceLocator locations)
        {
            if (kind == ActorVisualKind.WorldSprite)
                CheckAttributes(element, diagnostics, locations, "Provider", "Asset", "Resource", "Lifetime");
            else if (kind == ActorVisualKind.Portrait)
                CheckAttributes(element, diagnostics, locations, "Id", "Provider", "Asset", "Resource", "Lifetime", "LegacyPerson");
            else
                CheckAttributes(element, diagnostics, locations, "Id", "Provider", "Asset", "Resource", "Lifetime");

            CheckNoTextContent(element, diagnostics, locations);

            bool valid = true;

            // WorldSprite 每个人物只有一个，没有 Id 属性，固定使用 default 作为局部 ID。
            string id = "default";
            if (kind != ActorVisualKind.WorldSprite)
            {
                XAttribute idAttribute = element.Attribute("Id");
                if (idAttribute == null)
                {
                    diagnostics.AddError(InvalidActorId, $"`{element.Name.LocalName}` 缺少 `Id` 属性。", locations.ForElement(element));
                    valid = false;
                }
                else if (!ActorNaming.IsValidLocalId(idAttribute.Value))
                {
                    diagnostics.AddError(
                        InvalidActorId,
                        $"`{element.Name.LocalName}` 的 ID `{idAttribute.Value}` 不合法：只能以小写字母开头，后接小写字母、数字、`-` 或 `_`。",
                        locations.ForAttribute(idAttribute));
                    valid = false;
                }
                else
                {
                    id = idAttribute.Value;
                }
            }

            ActorVisualResource resource = ReadDeclaredResource(element, kind, isBuiltIn, diagnostics, locations);
            if (resource == null)
                valid = false;

            ActorVisualLifetime lifetime = ActorVisualLifetime.Event;
            XAttribute lifetimeAttribute = element.Attribute("Lifetime");
            if (lifetimeAttribute != null)
            {
                if (lifetimeAttribute.Value == "event")
                    lifetime = ActorVisualLifetime.Event;
                else if (lifetimeAttribute.Value == "static")
                    lifetime = ActorVisualLifetime.Static;
                else
                {
                    diagnostics.AddError(
                        InvalidXml,
                        $"`Lifetime` 只能是 `event` 或 `static`，实际为 `{lifetimeAttribute.Value}`。",
                        locations.ForAttribute(lifetimeAttribute));
                    valid = false;
                }
            }

            string legacyPerson = null;
            if (kind == ActorVisualKind.Portrait)
                legacyPerson = ReadLegacyPerson(element, isBuiltIn, diagnostics, locations, ref valid);

            if (!valid)
                return null;

            return new ActorVisual(id, kind, resource, legacyPerson, lifetime, locations.ForElement(element));
        }

        private static string ReadLegacyPerson(
            XElement element,
            bool isBuiltIn,
            DiagnosticBag diagnostics,
            SourceLocator locations,
            ref bool valid)
        {
            XAttribute attribute = element.Attribute("LegacyPerson");
            if (attribute == null)
                return null;

            if (!isBuiltIn)
            {
                diagnostics.AddError(
                    ForbiddenLegacyAlias,
                    "只有 Polaris 内置固定目录可以声明 `LegacyPerson`，避免模组抢占原版短键。",
                    locations.ForAttribute(attribute));
                valid = false;
                return null;
            }

            if (!ActorNaming.IsValidLegacyPerson(attribute.Value))
            {
                diagnostics.AddError(
                    ForbiddenLegacyAlias,
                    $"原版短键 `{attribute.Value}` 不合法：只能是不含空白与 `:` 的 ASCII 可见字符。",
                    locations.ForAttribute(attribute));
                valid = false;
                return null;
            }

            return attribute.Value;
        }

        /// <summary>
        /// 读取显式声明 <c>Provider</c> 的视觉资源。<c>game-pxls</c> 必须写 <c>Asset</c>，
        /// <c>polaris-res</c> 必须写 <c>Resource</c>；两个属性同时出现或都不出现都是 PEVT9110。
        /// </summary>
        private static ActorVisualResource ReadDeclaredResource(
            XElement element,
            ActorVisualKind kind,
            bool isBuiltIn,
            DiagnosticBag diagnostics,
            SourceLocator locations)
        {
            XAttribute providerAttribute = element.Attribute("Provider");
            if (providerAttribute == null)
            {
                diagnostics.AddError(InvalidProvider, $"`{element.Name.LocalName}` 缺少 `Provider` 属性。", locations.ForElement(element));
                return null;
            }

            XAttribute assetAttribute = element.Attribute("Asset");
            XAttribute resourceAttribute = element.Attribute("Resource");

            if (providerAttribute.Value == ProviderGamePxls)
            {
                if (!isBuiltIn)
                {
                    diagnostics.AddError(
                        InvalidProvider,
                        $"`{ProviderGamePxls}` 只能在 Polaris 内置目录中使用。",
                        locations.ForAttribute(providerAttribute));
                    return null;
                }

                if (resourceAttribute != null || assetAttribute == null)
                {
                    diagnostics.AddError(
                        InvalidResourceReference,
                        $"`{ProviderGamePxls}` 必须且只能使用 `Asset` 指定原版 Bundle 逻辑路径。",
                        locations.ForAttribute(resourceAttribute ?? providerAttribute));
                    return null;
                }

                if (!IsValidGameAssetPath(assetAttribute.Value))
                {
                    diagnostics.AddError(
                        InvalidResourceReference,
                        $"原版资源路径 `{assetAttribute.Value}` 不合法：只接受由 `/` 分隔的相对逻辑路径，不接受绝对路径或 `..`。",
                        locations.ForAttribute(assetAttribute));
                    return null;
                }

                return ActorVisualResource.FromGameAsset(assetAttribute.Value);
            }

            if (providerAttribute.Value == ProviderPolarisRes)
            {
                if (isBuiltIn)
                {
                    diagnostics.AddError(
                        InvalidProvider,
                        $"`{ProviderPolarisRes}` 只能在自定义目录中使用。",
                        locations.ForAttribute(providerAttribute));
                    return null;
                }

                if (assetAttribute != null || resourceAttribute == null)
                {
                    diagnostics.AddError(
                        InvalidResourceReference,
                        $"`{ProviderPolarisRes}` 必须且只能使用 `Resource` 指定 PolarisRes 静态字段。",
                        locations.ForAttribute(assetAttribute ?? providerAttribute));
                    return null;
                }

                if (!IsValidFieldReference(resourceAttribute.Value))
                {
                    diagnostics.AddError(
                        InvalidResourceReference,
                        $"资源引用 `{resourceAttribute.Value}` 不合法：只接受 `类型全名.字段名` 形式的点分标识符，不接受方法调用、泛型或索引。",
                        locations.ForAttribute(resourceAttribute));
                    return null;
                }

                return ActorVisualResource.FromPolarisResField(resourceAttribute.Value);
            }

            diagnostics.AddError(
                InvalidProvider,
                $"未登记的视觉提供者 `{providerAttribute.Value}`；第一版只有 `{ProviderGamePxls}` 与 `{ProviderPolarisRes}`。",
                locations.ForAttribute(providerAttribute));
            _ = kind;
            return null;
        }

        /// <summary>
        /// <c>Actor.Icon</c> 没有自己的 <c>Provider</c> 属性，其提供者由目录来源决定：
        /// 内置目录借用原版资源名，自定义目录引用 PolarisRes 字段。
        /// </summary>
        private static ActorVisualResource ReadInheritedResource(
            XAttribute attribute,
            bool isBuiltIn,
            DiagnosticBag diagnostics,
            SourceLocator locations)
        {
            if (isBuiltIn)
            {
                if (IsValidGameAssetPath(attribute.Value))
                    return ActorVisualResource.FromGameAsset(attribute.Value);

                diagnostics.AddError(
                    InvalidResourceReference,
                    $"原版图标资源 `{attribute.Value}` 不合法。",
                    locations.ForAttribute(attribute));
                return null;
            }

            if (IsValidFieldReference(attribute.Value))
                return ActorVisualResource.FromPolarisResField(attribute.Value);

            diagnostics.AddError(
                InvalidResourceReference,
                $"图标资源引用 `{attribute.Value}` 不合法：只接受 `类型全名.字段名` 形式的点分标识符。",
                locations.ForAttribute(attribute));
            return null;
        }

        private static ActorAppearance ReadAppearance(XElement element, DiagnosticBag diagnostics, SourceLocator locations)
        {
            CheckAttributes(element, diagnostics, locations, "Id", "Portrait", "Pose", "Frame");
            CheckNoTextContent(element, diagnostics, locations);

            bool valid = true;

            string id = element.Attribute("Id")?.Value;
            if (id == null)
            {
                diagnostics.AddError(InvalidActorId, "`Appearance` 缺少 `Id` 属性。", locations.ForElement(element));
                valid = false;
            }
            else if (!ActorNaming.IsValidLocalId(id))
            {
                diagnostics.AddError(
                    InvalidActorId,
                    $"appearance ID `{id}` 不合法：只能以小写字母开头，后接小写字母、数字、`-` 或 `_`。",
                    locations.ForAttribute(element.Attribute("Id")));
                valid = false;
            }

            string portraitId = element.Attribute("Portrait")?.Value;
            string pose = element.Attribute("Pose")?.Value;
            string frame = element.Attribute("Frame")?.Value;

            if (string.IsNullOrEmpty(portraitId) || string.IsNullOrEmpty(pose) || string.IsNullOrEmpty(frame))
            {
                diagnostics.AddError(
                    UnknownVisualReference,
                    "`Appearance` 必须同时提供非空的 `Portrait`、`Pose` 与 `Frame`。",
                    locations.ForElement(element));
                valid = false;
            }

            if (!valid)
                return null;

            return new ActorAppearance(id, portraitId, pose, frame, locations.ForElement(element));
        }

        private static ActorAnchor ReadAnchor(XElement element, DiagnosticBag diagnostics, SourceLocator locations)
        {
            CheckAttributes(element, diagnostics, locations, "Id", "X", "Y", "EnterX", "EnterY");
            CheckNoTextContent(element, diagnostics, locations);

            bool valid = true;

            string id = element.Attribute("Id")?.Value;
            if (id == null)
            {
                diagnostics.AddError(InvalidActorId, "`Anchor` 缺少 `Id` 属性。", locations.ForElement(element));
                valid = false;
            }
            else if (!ActorNaming.IsValidLocalId(id))
            {
                diagnostics.AddError(
                    InvalidActorId,
                    $"anchor ID `{id}` 不合法：只能以小写字母开头，后接小写字母、数字、`-` 或 `_`。",
                    locations.ForAttribute(element.Attribute("Id")));
                valid = false;
            }

            float x = ReadCoordinate(element, "X", required: true, diagnostics, locations, ref valid) ?? 0f;
            float y = ReadCoordinate(element, "Y", required: true, diagnostics, locations, ref valid) ?? 0f;
            float? enterX = ReadCoordinate(element, "EnterX", required: false, diagnostics, locations, ref valid);
            float? enterY = ReadCoordinate(element, "EnterY", required: false, diagnostics, locations, ref valid);

            if (enterX.HasValue != enterY.HasValue)
            {
                diagnostics.AddError(
                    InvalidAnchor,
                    "`EnterX` 与 `EnterY` 必须同时声明或同时省略。",
                    locations.ForElement(element));
                valid = false;
            }

            if (!valid)
                return null;

            return new ActorAnchor(id, x, y, enterX, enterY, locations.ForElement(element));
        }

        private static float? ReadCoordinate(
            XElement element,
            string attributeName,
            bool required,
            DiagnosticBag diagnostics,
            SourceLocator locations,
            ref bool valid)
        {
            XAttribute attribute = element.Attribute(attributeName);
            if (attribute == null)
            {
                if (required)
                {
                    diagnostics.AddError(InvalidAnchor, $"`Anchor` 缺少 `{attributeName}` 属性。", locations.ForElement(element));
                    valid = false;
                }

                return null;
            }

            // NumberStyles.Float 不含 AllowThousands，但 InvariantCulture 下 "NaN"/"Infinity" 仍可能被接受，
            // 因此解析后再做一次有限值判定，保证"非有限 anchor"有确定结果。
            if (!float.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || !ActorAnchor.IsFinite(value))
            {
                diagnostics.AddError(
                    InvalidAnchor,
                    $"`{attributeName}=\"{attribute.Value}\"` 不是有限的十进制数。",
                    locations.ForAttribute(attribute));
                valid = false;
                return null;
            }

            return value;
        }

        private static bool AddUnique<T>(
            List<T> target,
            T item,
            Func<T, string> keySelector,
            string category,
            XElement element,
            DiagnosticBag diagnostics,
            SourceLocator locations)
        {
            string key = keySelector(item);
            foreach (T existing in target)
            {
                if (ActorNaming.IdComparer.Equals(keySelector(existing), key))
                {
                    diagnostics.AddError(
                        DuplicateVisualId,
                        $"同一人物内 `{category}` 的 ID `{key}` 重复。",
                        locations.ForAttribute(element.Attribute("Id")) ?? locations.ForElement(element));
                    return false;
                }
            }

            target.Add(item);
            return true;
        }

        private static void CheckAttributes(XElement element, DiagnosticBag diagnostics, SourceLocator locations, params string[] allowed)
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;

                bool ok = attribute.Name.Namespace == XNamespace.None;
                if (ok)
                {
                    ok = false;
                    foreach (string name in allowed)
                    {
                        if (string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))
                        {
                            ok = true;
                            break;
                        }
                    }
                }

                if (!ok)
                {
                    diagnostics.AddError(
                        InvalidXml,
                        $"`{element.Name.LocalName}` 上不允许属性 `{attribute.Name}`。",
                        locations.ForAttribute(attribute));
                }
            }
        }

        /// <summary>`.pactor` 的全部数据都写在属性里；元素正文一律拒绝，避免混入脚本或 CMD 文本。</summary>
        private static void CheckNoTextContent(XElement element, DiagnosticBag diagnostics, SourceLocator locations)
        {
            foreach (XNode node in element.Nodes())
            {
                if (node is XText text && !string.IsNullOrWhiteSpace(text.Value))
                {
                    diagnostics.AddError(
                        InvalidXml,
                        $"`{element.Name.LocalName}` 不允许包含正文内容。",
                        locations.ForNode(node));
                }
                else if (node is XCData)
                {
                    diagnostics.AddError(InvalidXml, $"`{element.Name.LocalName}` 不允许包含 CDATA。", locations.ForNode(node));
                }
            }
        }

        /// <summary>原版 Bundle 逻辑路径：由 `/` 分隔的相对路径段，段内只允许 ASCII 字母、数字、`_`、`.` 与 `-`。</summary>
        private static bool IsValidGameAssetPath(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            if (value[0] == '/' || value[value.Length - 1] == '/')
                return false;

            int segmentStart = 0;
            for (int i = 0; i <= value.Length; i++)
            {
                if (i != value.Length && value[i] != '/')
                    continue;

                int length = i - segmentStart;
                if (length == 0)
                    return false;

                string segment = value.Substring(segmentStart, length);
                if (segment == "." || segment == "..")
                    return false;

                foreach (char c in segment)
                {
                    bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                        || c == '_' || c == '.' || c == '-';
                    if (!ok)
                        return false;
                }

                segmentStart = i + 1;
            }

            return true;
        }

        /// <summary>PolarisRes 字段引用：至少两段的点分 ASCII 标识符（类型全名 + 字段名），不接受任何调用或泛型语法。</summary>
        private static bool IsValidFieldReference(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            int segmentCount = 0;
            int segmentStart = 0;
            for (int i = 0; i <= value.Length; i++)
            {
                if (i != value.Length && value[i] != '.')
                    continue;

                int length = i - segmentStart;
                if (length == 0)
                    return false;

                char first = value[segmentStart];
                bool firstOk = (first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z') || first == '_';
                if (!firstOk)
                    return false;

                for (int k = segmentStart + 1; k < i; k++)
                {
                    char c = value[k];
                    bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                    if (!ok)
                        return false;
                }

                segmentCount++;
                segmentStart = i + 1;
            }

            return segmentCount >= 2;
        }

        /// <summary>
        /// 把 <see cref="IXmlLineInfo"/> 的 1-based 行列换算成 <see cref="TextLocation"/>。
        /// XML 解析器和 <see cref="LineMap"/> 都把 <c>\r\n</c>、单独 <c>\r</c>、单独 <c>\n</c> 视为一个换行，
        /// 因此两侧行号一致；越界时退化为文件末尾的空跨度，不抛异常。
        /// </summary>
        private sealed class SourceLocator
        {
            private readonly SourceText _source;
            private readonly LineMap _lineMap;

            public SourceLocator(SourceText source)
            {
                _source = source;
                _lineMap = LineMap.Build(source.Content);
            }

            public TextLocation WholeFile() => _source.GetLocation(new TextSpan(0, 0));

            public TextLocation FromLineInfo(int line, int column, int length)
            {
                if (line <= 0 || line > _lineMap.LineCount || column <= 0)
                    return WholeFile();

                int start = _lineMap.GetLineStart(line - 1) + (column - 1);
                if (start < 0 || start > _source.Length)
                    return WholeFile();

                int clamped = Math.Max(0, Math.Min(length, _source.Length - start));
                return _source.GetLocation(new TextSpan(start, clamped));
            }

            public TextLocation ForElement(XElement element) => ForObject(element, element.Name.LocalName.Length);

            public TextLocation ForNode(XNode node) => ForObject(node, 1);

            public TextLocation ForAttribute(XAttribute attribute) =>
                attribute == null ? null : ForObject(attribute, attribute.Name.LocalName.Length);

            private TextLocation ForObject(XObject obj, int length)
            {
                var lineInfo = (IXmlLineInfo)obj;
                if (!lineInfo.HasLineInfo())
                    return WholeFile();
                return FromLineInfo(lineInfo.LineNumber, lineInfo.LinePosition, length);
            }
        }
    }
}
