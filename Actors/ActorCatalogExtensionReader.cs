using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Text;

namespace Polaris.Pevt.Actors
{
    /// <summary>
    /// PEVT-E06 的扩展 sidecar 读取器。
    ///
    /// 与 `.pactor` 读取器同一个类：两者共用 XML 加载、属性白名单、正文禁止与 appearance 解析，
    /// 所以"什么算合法 appearance"不可能在基础目录和扩展之间漂移。
    /// </summary>
    public static partial class ActorCatalogReader
    {
        /// <summary>扩展 sidecar 的根元素名。</summary>
        private const string ExtensionRootName = "ActorCatalogExtension";

        /// <summary>
        /// <c>ActorExtension</c> 上明确禁止、但作者最可能顺手写上的属性。
        /// 它们不是"未知属性"而是"第一版不允许的覆盖"，所以要有自己的诊断和消息。
        /// </summary>
        private static readonly string[] ForbiddenExtensionAttributes =
        {
            "DisplayName", "DisplayKey", "Voice", "Color", "Icon", "DefaultPortrait", "LegacyPerson", "Namespace", "BuiltIn",
        };

        /// <summary>同理，<c>ActorExtension</c> 下只允许 <c>Appearance</c>。</summary>
        private static readonly string[] ForbiddenExtensionElements =
        {
            "Portrait", "UiPortrait", "WorldSprite", "Anchor", "Actor",
        };

        public static ActorCatalogExtensionReadResult ReadExtension(
            byte[] utf8Bytes,
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            if (utf8Bytes == null)
                throw new ArgumentNullException(nameof(utf8Bytes));

            SourceTextLoadResult loaded = SourceText.FromUtf8(utf8Bytes, sourcePath, cancellationToken);
            if (!loaded.Success)
            {
                var bag = new DiagnosticBag();
                bag.Add(new Diagnostic(InvalidXml, DiagnosticSeverity.Error,
                    "人物目录扩展不是合法的 UTF-8 文本，无法作为 XML 读取。", null));
                return new ActorCatalogExtensionReadResult(null, null, bag.ToReadOnly());
            }

            return ReadExtension(loaded.Text, cancellationToken);
        }

        /// <summary>测试与编辑器用的便捷入口。</summary>
        public static ActorCatalogExtensionReadResult ReadExtensionText(
            string xml,
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            if (xml == null)
                throw new ArgumentNullException(nameof(xml));
            return ReadExtension(new UTF8Encoding(false).GetBytes(xml), sourcePath, cancellationToken);
        }

        public static ActorCatalogExtensionReadResult ReadExtension(
            SourceText source,
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
                    return new ActorCatalogExtensionReadResult(null, source, diagnostics.ToReadOnly());

                ActorCatalogExtension extension = ReadExtensionRoot(document, source, diagnostics, locations, cancellationToken);
                return new ActorCatalogExtensionReadResult(
                    diagnostics.HasErrors ? null : extension,
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
                    $"读取人物目录扩展时发生内部异常：{ex.GetType().Name}: {ex.Message}",
                    locations.WholeFile()));
                return new ActorCatalogExtensionReadResult(null, source, diagnostics.ToReadOnly());
            }
        }

        private static ActorCatalogExtension ReadExtensionRoot(
            XDocument document,
            SourceText source,
            DiagnosticBag diagnostics,
            SourceLocator locations,
            CancellationToken cancellationToken)
        {
            XElement root = document.Root;
            if (root == null || root.Name != Ns + ExtensionRootName)
            {
                diagnostics.AddError(
                    InvalidXml,
                    $"根元素必须是命名空间 `{ActorCatalogExtension.XmlNamespace}` 下的 `{ExtensionRootName}`。",
                    root != null ? locations.ForElement(root) : locations.WholeFile());
                return null;
            }

            CheckAttributes(root, diagnostics, locations, "Version");
            CheckNoTextContent(root, diagnostics, locations);

            int version = ReadExtensionVersion(root, diagnostics, locations);

            var extensions = new List<ActorExtensionDefinition>();

            // 同一份 sidecar 里同一个人物只能出现一次：两段针对同一人物的追加合起来是一段，
            // 分开写就分不清"哪一段先应用"，而扩展的可追踪性正建立在应用顺序上。
            var seenActors = new HashSet<string>(ActorNaming.IdComparer);

            foreach (XElement child in root.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (child.Name != Ns + "ActorExtension")
                {
                    diagnostics.AddError(
                        InvalidXml,
                        $"`{ExtensionRootName}` 下不允许元素 `{child.Name.LocalName}`。",
                        locations.ForElement(child));
                    continue;
                }

                ActorExtensionDefinition extension = ReadActorExtension(child, diagnostics, locations);
                if (extension == null)
                    continue;

                if (!seenActors.Add(extension.ActorId))
                {
                    diagnostics.AddError(
                        DuplicateExtensionAppearance,
                        $"同一份扩展里对人物 `{extension.ActorId}` 出现了多段 `ActorExtension`；请合并成一段。",
                        locations.ForAttribute(child.Attribute("Actor")) ?? locations.ForElement(child));
                    continue;
                }

                extensions.Add(extension);
            }

            if (diagnostics.HasErrors || version != ActorCatalogExtension.SupportedVersion)
                return null;

            return new ActorCatalogExtension(version, source.FilePath, extensions);
        }

        private static int ReadExtensionVersion(XElement root, DiagnosticBag diagnostics, SourceLocator locations)
        {
            XAttribute attribute = root.Attribute("Version");
            if (attribute == null)
            {
                diagnostics.AddError(UnsupportedVersion, $"`{ExtensionRootName}` 缺少 `Version` 属性。", locations.ForElement(root));
                return -1;
            }

            if (!int.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int version)
                || version != ActorCatalogExtension.SupportedVersion)
            {
                diagnostics.AddError(
                    UnsupportedVersion,
                    $"不支持的扩展版本 `{attribute.Value}`；第一版只能为 {ActorCatalogExtension.SupportedVersion}。",
                    locations.ForAttribute(attribute));
                return -1;
            }

            return version;
        }

        private static ActorExtensionDefinition ReadActorExtension(
            XElement element, DiagnosticBag diagnostics, SourceLocator locations)
        {
            ReportForbiddenOverrides(element, diagnostics, locations);
            CheckAttributes(element, diagnostics, locations, "Actor");
            CheckNoTextContent(element, diagnostics, locations);

            bool valid = true;

            string actorId = element.Attribute("Actor")?.Value;
            if (string.IsNullOrEmpty(actorId))
            {
                diagnostics.AddError(InvalidActorId, "`ActorExtension` 缺少 `Actor` 属性。", locations.ForElement(element));
                valid = false;
            }
            else if (!ActorNaming.IsValidActorId(actorId))
            {
                diagnostics.AddError(
                    InvalidActorId,
                    $"扩展目标 `{actorId}` 不是合法的最终人物 ID：必须写成 `<namespace>:<local-id>`。",
                    locations.ForAttribute(element.Attribute("Actor")));
                valid = false;
            }

            var appearances = new List<ActorAppearance>();
            var seen = new HashSet<string>(ActorNaming.IdComparer);

            foreach (XElement child in element.Elements())
            {
                if (child.Name != Ns + "Appearance")
                {
                    // 未知元素已经在 ReportForbiddenOverrides / 这里各报一次的风险：
                    // 前者只认那几个"会让人以为能覆盖"的名字，其余落到这条通用错误上。
                    if (!IsForbiddenExtensionElement(child.Name.LocalName))
                    {
                        diagnostics.AddError(
                            InvalidXml,
                            $"`ActorExtension` 下不允许元素 `{child.Name.LocalName}`。",
                            locations.ForElement(child));
                    }

                    valid = false;
                    continue;
                }

                ActorAppearance appearance = ReadAppearance(child, diagnostics, locations);
                if (appearance == null)
                {
                    valid = false;
                    continue;
                }

                if (!seen.Add(appearance.Id))
                {
                    diagnostics.AddError(
                        DuplicateExtensionAppearance,
                        $"appearance ID `{appearance.Id}` 在同一段扩展内重复。",
                        locations.ForAttribute(child.Attribute("Id")) ?? locations.ForElement(child));
                    valid = false;
                    continue;
                }

                appearances.Add(appearance);
            }

            if (appearances.Count == 0 && valid)
            {
                diagnostics.AddError(
                    InvalidXml,
                    $"`ActorExtension`（`{actorId}`）没有追加任何 `Appearance`。",
                    locations.ForElement(element));
                valid = false;
            }

            return valid ? new ActorExtensionDefinition(actorId, appearances, locations.ForElement(element)) : null;
        }

        /// <summary>
        /// 把"想覆盖"这件事和"写错了"分开报。第一版明确不允许覆盖 Actor 元数据或替换视觉，
        /// 而作者最容易做的正是照抄一段 `.pactor` 过来，所以这几个名字要给出说明为什么不行的消息。
        /// </summary>
        private static void ReportForbiddenOverrides(XElement element, DiagnosticBag diagnostics, SourceLocator locations)
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;

                foreach (string forbidden in ForbiddenExtensionAttributes)
                {
                    if (!string.Equals(attribute.Name.LocalName, forbidden, StringComparison.Ordinal))
                        continue;

                    diagnostics.AddError(
                        ForbiddenExtensionOverride,
                        $"扩展不能声明 `{forbidden}`：第一版只允许追加 appearance，不允许覆盖人物元数据。",
                        locations.ForAttribute(attribute));
                }
            }

            foreach (XElement child in element.Elements())
            {
                if (!IsForbiddenExtensionElement(child.Name.LocalName))
                    continue;

                diagnostics.AddError(
                    ForbiddenExtensionOverride,
                    $"扩展不能声明 `{child.Name.LocalName}`：第一版只允许追加 `Appearance`。",
                    locations.ForElement(child));
            }
        }

        private static bool IsForbiddenExtensionElement(string localName)
        {
            foreach (string forbidden in ForbiddenExtensionElements)
            {
                if (string.Equals(localName, forbidden, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
