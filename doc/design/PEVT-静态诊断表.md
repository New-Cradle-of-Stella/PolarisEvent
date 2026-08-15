# PEVT 静态诊断表

本表记录 PolarisEvent 在加载 `.pevt` 源文件时产生的词法、语法、类型、名称和静态控制流诊断。PEVT 是解释型语言；这些诊断不会生成或校验 `.cmd` 产物，而是决定事件能否进入可执行状态。

## 编号分区

| 编号范围 | 分类 |
| --- | --- |
| `PEVT1xxx` | 词法、文件与事件声明 |
| `PEVT2xxx` | 流程语句与结束符 |
| `PEVT3xxx` | 标签与跳转 |
| `PEVT4xxx` | 控制流分析 |
| `PEVT5xxx` | 表达式（保留） |
| `PEVT6xxx` | 变量（保留） |
| `PEVT7xxx` | 内置语句、自定义事件块、异步、事件间调用与动态执行 |
| `PEVT8xxx` | 原始调用 |
| `PEVT9xxx` | 加载器与静态分析器内部错误 |

## 词法与文件结构

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT1001` | `UnexpectedToken` | Error | 出现当前语法不能识别的字符或 token。 |
| `PEVT1002` | `UnterminatedString` | Error | 双引号字符串到行末或文件末尾仍未闭合。 |
| `PEVT1003` | `EventBlockNotSupported` | Error | 出现 `{` 或 `}`；当前语法不支持事件块。 |
| `PEVT1004` | `UnterminatedCharacter` | Error | 单引号字符到行末或文件末尾仍未闭合。 |
| `PEVT1005` | `MultipleStatementsOnLine` | Error | 除 `$raw` 原始文本块和合法多行字符串外，同一物理行中出现多条普通语句。 |
| `PEVT1006` | `UnexpectedLineBreak` | Error | 普通表达式、参数列表、定义签名或流程控制行在完成前发生不属于合法多行字符串的换行。 |
| `PEVT1007` | `SemicolonStatementTerminator` | Error | 使用分号结束或分隔 PEVT 普通语句。 |
| `PEVT1008` | `InvalidIdentifier` | Error | 标识符或调用名称不符合规定的 ASCII 字母、数字和下划线格式。 |
| `PEVT1009` | `InvalidSourceEncoding` | Error | 源文件不是合法 UTF-8，或 BOM 出现在文件起始位置以外。 |
| `PEVT1010` | `UnterminatedBlockComment` | Error | `/*` 块注释到文件末尾仍未遇到结束分隔符 `*/`。 |
| `PEVT1011` | `MisalignedMultilineString` | Error | 多行字符串某个续接段的开始双引号没有与第一段字符串的开始双引号位于同一源文件列。 |
| `PEVT1012` | `InvalidMultilineStringContinuation` | Error | 字符串行末 `+` 后的下一物理行不是直接续接的字符串字面量，续接缩进使用了制表符，或各段之间插入了空行或注释。 |
| `PEVT1013` | `UnterminatedMultilineStringContinuation` | Error | 字符串以行末 `+` 请求继续，但在文件末尾前没有出现所需的下一段字符串字面量。 |
| `PEVT1101` | `MissingEventId` | Error | 文件没有 `id` 声明。 |
| `PEVT1102` | `EventIdNotFirst` | Error | `id` 不是文件的第一个语法语句。 |
| `PEVT1103` | `DuplicateEventId` | Error | 同一文件出现多个 `id` 声明。 |
| `PEVT1104` | `MissingEventIdValue` | Error | `id` 后没有事件 ID。 |
| `PEVT1105` | `EventIdMustBeString` | Error | `id` 后的事件 ID 不是双引号字符串。 |
| `PEVT1106` | `UnexpectedEventIdArgument` | Error | 事件 ID 字符串后还有额外参数。 |
| `PEVT1107` | `InvalidEnablePlacement` | Error | `enable` 能力声明没有位于紧跟 `id` 的连续能力声明区域中。 |
| `PEVT1108` | `DuplicateEnableDeclaration` | Error | 同一文件重复声明相同的 `cs` 或 `async` 能力。 |
| `PEVT1109` | `InvalidEnabledCapability` | Error | `enable` 后不是精确的 `cs` 或 `async`，或能力名称后还有额外参数。 |
| `PEVT1110` | `EmptyEventId` | Error | 事件 ID 双引号内没有任何字符。 |
| `PEVT1111` | `InvalidEventIdCharacter` | Error | 事件 ID 包含 ASCII 字母、数字和 Unicode 中文汉字以外的字符。 |
| `PEVT1201` | `UnknownStatement` | Error | 行首内容不属于任何已定义的事件语句。 |

## 条件语句

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT2001` | `MissingIfExpression` | Error | `if` 后没有表达式。 |
| `PEVT2002` | `MissingEndIf` | Error | `if` 没有对应的 `endif`。 |
| `PEVT2003` | `OrphanElif` | Error | `elif` 不属于任何尚未闭合的 `if`。 |
| `PEVT2004` | `MissingElifExpression` | Error | `elif` 后没有表达式。 |
| `PEVT2005` | `ElifAfterElse` | Error | 同一个 `if` 中的 `elif` 出现在 `else` 之后。 |
| `PEVT2006` | `OrphanElse` | Error | `else` 不属于任何尚未闭合的 `if`。 |
| `PEVT2007` | `DuplicateElse` | Error | 同一个 `if` 中出现多个 `else`。 |
| `PEVT2008` | `ElseHasExpression` | Error | `else` 后出现表达式或其他参数。 |
| `PEVT2009` | `OrphanEndIf` | Error | `endif` 没有对应的 `if`。 |
| `PEVT2010` | `EndIfHasArguments` | Error | `endif` 后出现参数。 |

## 循环语句

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT2101` | `MissingWhileExpression` | Error | `while` 后没有表达式。 |
| `PEVT2102` | `MissingEndWhile` | Error | `while` 没有对应的 `endwhile`。 |
| `PEVT2103` | `OrphanEndWhile` | Error | `endwhile` 没有对应的 `while`。 |
| `PEVT2104` | `EndWhileHasArguments` | Error | `endwhile` 后出现参数。 |

## 事件结束语句

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT2201` | `EndHasArguments` | Error | `end` 后出现参数。 |

## 空流程语句体

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT2301` | `EmptyIfBody` | Warning | `if` 内没有事件语句。 |
| `PEVT2302` | `EmptyElifBody` | Warning | `elif` 内没有事件语句。 |
| `PEVT2303` | `EmptyElseBody` | Warning | `else` 内没有事件语句。 |
| `PEVT2304` | `EmptyWhileBody` | Warning | `while` 内没有事件语句。 |

## 选择语句

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT2401` | `MissingSwitchExpression` | Error | `switch` 后没有表达式。 |
| `PEVT2402` | `MissingEndSwitch` | Error | `switch` 没有对应的 `endswitch`。 |
| `PEVT2403` | `EmptySwitch` | Error | `switch` 中没有 `case` 或 `default`。 |
| `PEVT2404` | `SwitchMustStartWithArm` | Error | `switch` 后的第一条语句不是 `case` 或 `default`。 |
| `PEVT2405` | `OrphanCase` | Error | `case` 不属于任何尚未闭合的 `switch`。 |
| `PEVT2406` | `MissingCaseExpression` | Error | `case` 后没有表达式。 |
| `PEVT2407` | `DuplicateCaseExpression` | Error | 同一个 `switch` 中存在完全相同的 `case` 表达式。 |
| `PEVT2408` | `EmptyCaseBody` | Warning | `case` 内没有事件语句。 |
| `PEVT2409` | `OrphanDefault` | Error | `default` 不属于任何尚未闭合的 `switch`。 |
| `PEVT2410` | `DuplicateDefault` | Error | 同一个 `switch` 中出现多个 `default`。 |
| `PEVT2411` | `DefaultHasExpression` | Error | `default` 后出现表达式或其他参数。 |
| `PEVT2412` | `EmptyDefaultBody` | Warning | `default` 内没有事件语句。 |
| `PEVT2413` | `OrphanEndSwitch` | Error | `endswitch` 没有对应的 `switch`。 |
| `PEVT2414` | `EndSwitchHasArguments` | Error | `endswitch` 后出现参数。 |
| `PEVT2415` | `SideEffectingCaseExpression` | Error | `case` 表达式包含 `@`、`_`、`$raw cs`、`await` 或 `status` 等不允许的有副作用或运行时操作。 |

## 标签

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT3001` | `MissingLabelName` | Error | `#` 后没有标签标识符。 |
| `PEVT3002` | `InvalidLabelName` | Error | `#` 后的内容不是合法标识符。 |
| `PEVT3003` | `DuplicateLabel` | Error | 同一事件重复声明相同标签。 |

## `goto`

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT3101` | `MissingGotoTarget` | Error | `goto` 后没有跳转目标。 |
| `PEVT3102` | `GotoTargetMustBeLabel` | Error | `goto` 的目标没有使用 `#LabelName` 形式。 |
| `PEVT3103` | `InvalidGotoTarget` | Error | `goto #` 后的内容不是合法标识符。 |
| `PEVT3104` | `UndefinedLabel` | Error | `goto` 引用的标签未在当前事件中声明。 |
| `PEVT3105` | `UnexpectedGotoArgument` | Error | 标签引用后还有额外参数。 |
| `PEVT3106` | `GotoAcrossEventEnvironment` | Error | `goto` 的来源与目标分别位于文件外层事件和自定义事件块，或位于不同自定义事件块。 |
| `PEVT3107` | `GotoIntoStructuredFlow` | Error | 目标标签的结构路径不是 `goto` 来源结构路径的前缀；跳转试图进入更深结构、兄弟分支或其他不属于来源路径的结构。 |
| `PEVT3111` | `CaseGotoOutsideSwitch` | Error | `goto 表达式` 出现在 `switch` 之外。 |
| `PEVT3112` | `UndefinedCaseTarget` | Error | `goto 表达式` 没有匹配当前 `switch` 中表达式完全相同的 `case`。 |

## 控制流

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT4001` | `EventPathWithoutEnd` | Error | 存在可达的事件退出路径没有以 `end` 终止。 |
| `PEVT4002` | `UnreachableStatement` | Warning | 事件语句位于同一路径的 `end` 或无条件 `goto` 之后，且没有其他路径可以到达。 |

## 表达式

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT5001` | `InvalidExpression` | Error | token 序列不能组成合法表达式。 |
| `PEVT5002` | `MissingLeftOperand` | Error | 除语法位置允许的一元负号外，二元运算符左侧没有表达式。 |
| `PEVT5003` | `MissingRightOperand` | Error | 运算符右侧没有表达式。 |
| `PEVT5004` | `ExpressionTypeMismatch` | Error | 二元运算符左右两侧的表达式类型不同。 |
| `PEVT5005` | `OperatorNotDefinedForType` | Error | 运算符不能用于当前操作数类型。 |
| `PEVT5006` | `AssignmentTargetMustBeVariable` | Error | `=` 左侧不是已定义且可写的变量。 |
| `PEVT5007` | `LogicalOperandMustBeBool` | Error | `&`、`|`、`^` 或 `!` 的操作数不是 `bool`。 |
| `PEVT5008` | `ConditionMustBeBool` | Error | `if`、`elif` 或 `while` 的条件表达式结果不是 `bool`。 |
| `PEVT5009` | `SwitchCaseTypeMismatch` | Error | `switch` 表达式与某个 `case` 表达式的类型不同。 |
| `PEVT5010` | `UnknownType` | Error | 使用了 `int`、`float`、`bool`、`char`、`string` 以外的类型。 |
| `PEVT5011` | `OrderedComparisonRequiresNumber` | Error | `<`、`<=`、`>=` 或 `>` 的操作数不是 `int` 或 `float`。 |
| `PEVT5012` | `InvalidConversion` | Error | 使用了 `int → float`、`char → string` 以外的类型转换。 |
| `PEVT5013` | `ConversionTargetMustBeVariable` | Error | 转换标记后不是一个已定义变量。 |
| `PEVT5014` | `ConversionMustBeAdjacent` | Error | 转换标记与变量名之间存在空格或其他 token。 |
| `PEVT5015` | `UnterminatedGroupedExpression` | Error | 左括号 `(` 没有对应的右括号 `)`。 |
| `PEVT5016` | `EmptyGroupedExpression` | Error | 括号内没有表达式。 |
| `PEVT5017` | `IntegerLiteralOutOfRange` | Error | 整数字面量超出标准 32 位有符号整数范围。 |
| `PEVT5018` | `FloatLiteralOutOfRange` | Error | 浮点数字面量超出 IEEE 754 单精度有限值范围。 |
| `PEVT5019` | `MalformedNumericLiteral` | Error | 数值使用了无效的小数点、指数、类型后缀或数字分隔符形式。 |
| `PEVT5020` | `InvalidCharacterLiteral` | Error | 字符字面量解析后不是恰好一个字符。 |
| `PEVT5021` | `InvalidLiteralEscape` | Error | 字符串或字符字面量使用了未定义的转义形式。 |
| `PEVT5022` | `AssignmentUsedAsExpression` | Error | 赋值语句被嵌入初始化器、条件、调用参数、运算表达式或其他赋值语句。 |
| `PEVT5023` | `InvalidBooleanLiteral` | Error | 使用了 `true`、`false` 以外的大小写或数值形式表示布尔字面量。 |
| `PEVT5024` | `NumericNegationRequiresNumber` | Error | 一元负号 `-` 的操作数结果类型不是 `int` 或 `float`。 |

## 变量

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT6001` | `UndefinedVariable` | Error | 当前环境中不存在位于使用位置之前、且在所有可达路径上均已执行的变量或常量声明。 |
| `PEVT6002` | `ReadOnlyVariableAssignment` | Error | 赋值目标变量不可写。 |
| `PEVT6003` | `UninitializedVariable` | Error | 普通变量被读取时，存在到达当前位置但尚未完成赋值的可达路径；异步异常产生的未赋值结果由 PolarisEvent 在运行时处理。 |
| `PEVT6004` | `MissingDeclarationName` | Error | `var` 或 `const` 后没有变量名或常量名。 |
| `PEVT6005` | `MissingTypeAnnotation` | Error | 变量或常量声明没有使用 `:` 显式指定后置类型。 |
| `PEVT6006` | `InvalidTypePosition` | Error | 类型写在变量名或常量名前，或没有位于 `:` 之后。 |
| `PEVT6007` | `DuplicateDeclaration` | Error | 当前变量环境中重复声明相同名称的变量或常量。 |
| `PEVT6008` | `InitializerTypeMismatch` | Error | 初始化表达式类型与显式声明类型不同。 |
| `PEVT6009` | `MissingConstInitializer` | Error | `const` 声明没有在同一语句中使用 `=` 初始化。 |
| `PEVT6010` | `ConstantAssignment` | Error | 对已经初始化的 `const` 再次赋值。 |
| `PEVT6011` | `MissingInitializerExpression` | Error | 声明中的 `=` 后没有初始化表达式。 |
| `PEVT6012` | `VariableOutsideCurrentEnvironment` | Error | 引用了其他自定义事件块或外层事件环境中的变量、常量或参数。 |
| `PEVT6013` | `ReservedKeywordAsIdentifier` | Error | 保留关键字被用作变量、常量、参数、句柄或集合等待结果变量的名称。 |

## 内置事件语句

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT7001` | `MissingBuiltinName` | Error | `@` 后没有内置事件语句名称。 |
| `PEVT7002` | `UnknownBuiltinStatement` | Error | `@` 后的名称未登记在内置事件语句 API 表中。 |
| `PEVT7003` | `InvalidBuiltinCallSyntax` | Error | 内置事件语句调用不符合 `@名称(参数...)` 语法。 |
| `PEVT7004` | `MissingArgumentList` | Error | 内置事件语句名称后没有 `()` 参数列表。 |
| `PEVT7005` | `ArgumentCountMismatch` | Error | 实参数量与 API 签名中的形参数量不同。 |
| `PEVT7006` | `ArgumentTypeMismatch` | Error | 实参表达式类型与对应形参类型不同。 |
| `PEVT7007` | `NoMatchingBuiltinSignature` | Error | 找不到名称和参数签名完全匹配的内置事件语句 API。 |
| `PEVT7008` | `VoidBuiltinUsedAsExpression` | Error | 没有返回值类型的同步纯调用被用于表达式。 |
| `PEVT7009` | `BuiltinReturnTypeMismatch` | Error | API 返回值类型与接收位置要求的类型不同。 |
| `PEVT7010` | `InvalidBuiltinSignature` | Error | API 表中的参数或返回值签名不合法。 |

## 自定义事件块

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT7101` | `MissingCustomBlockName` | Error | `_` 后没有自定义事件块名称。 |
| `PEVT7102` | `InvalidCustomBlockSignature` | Error | 自定义事件块定义不符合 `[async] block _名称(参数名 : 类型, ...) [: 返回类型]` 语法。 |
| `PEVT7103` | `DuplicateCustomBlockDefinition` | Error | 重复定义同名自定义事件块。 |
| `PEVT7104` | `NestedCustomBlockDefinition` | Error | 在另一个自定义事件块内部定义自定义事件块。 |
| `PEVT7105` | `ReturnOutsideCustomBlock` | Error | `return` 位于任何自定义事件块之外。 |
| `PEVT7106` | `MissingCustomBlockReturnValue` | Error | 声明了返回值类型的自定义事件块使用了没有返回目标的 `return`。 |
| `PEVT7107` | `UnexpectedCustomBlockReturnValue` | Error | 未声明返回值类型的自定义事件块在 `return` 后提供了返回目标。 |
| `PEVT7108` | `InvalidCustomBlockReturnTarget` | Error | `return` 后不是一个已定义变量或常量的名称。 |
| `PEVT7109` | `CustomBlockReturnTypeMismatch` | Error | `return` 目标的类型与自定义事件块声明的返回值类型不同。 |
| `PEVT7110` | `UnknownCustomBlock` | Error | 调用了尚未定义的自定义事件块。 |
| `PEVT7111` | `MissingCustomBlockPrefix` | Error | 调用已知自定义事件块时省略了名称开头的 `_`。 |
| `PEVT7112` | `CustomBlockArgumentCountMismatch` | Error | 调用实参数量与自定义事件块定义的形参数量不同。 |
| `PEVT7113` | `CustomBlockArgumentTypeMismatch` | Error | 调用实参类型与对应形参类型不同。 |
| `PEVT7114` | `VoidCustomBlockUsedAsExpression` | Error | 无返回值的同步自定义事件块被用于表达式。 |
| `PEVT7115` | `CustomBlockUsedBeforeDefinition` | Error | 在自定义事件块完成定义之前调用该事件块。 |
| `PEVT7116` | `MissingCustomBlockEnd` | Error | 自定义事件块没有使用 `endblock` 显式闭合。 |
| `PEVT7117` | `CustomBlockNotAllPathsReturn` | Error | 声明了返回值类型的自定义事件块存在没有执行有返回值 `return` 就到达 `endblock` 的可达路径。 |
| `PEVT7118` | `UnexpectedCustomBlockEnd` | Error | `endblock` 位于任何自定义事件块之外，或被用于闭合其他流程语句。 |
| `PEVT7119` | `MissingCustomBlockKeyword` | Error | 看似自定义事件块定义的签名前缺少 `block` 关键字。 |
| `PEVT7120` | `EndInsideCustomBlock` | Error | 自定义事件块内部使用了只允许终止文件外层事件的 `end`。 |

## 事件间调用

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT7301` | `MissingEventCallTarget` | Error | `callevt` 后没有事件 ID 字面量。 |
| `PEVT7302` | `InvalidEventCallTarget` | Error | `callevt` 的目标不是符合事件 ID 字符规则的非空双引号字面量。 |
| `PEVT7303` | `DynamicEventCallTarget` | Error | `callevt` 使用变量、表达式或其他动态值作为目标事件 ID。 |
| `PEVT7304` | `EventCallUsedAsExpression` | Error | `callevt` 被用于普通变量、常量、运算、调用参数或其他普通表达式位置。 |
| `PEVT7305` | `AsyncModifierOnEventCall` | Error | 调用位置写出 `async callevt`；事件是否异步只能由目标文件的 `enable async` 标记决定。 |

> 加载器不得因为当前已加载事件中找不到 `callevt` 的目标事件 ID 而产生诊断；目标存在性及 `enable async` 标记均由 PolarisEvent 在调用时解析。

## 动态 PEVT 执行

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT7401` | `MissingExecArgument` | Error | `exec()` 没有提供参数。 |
| `PEVT7402` | `ExecArgumentCountMismatch` | Error | `exec(...)` 的参数数量不是一个。 |
| `PEVT7403` | `ExecArgumentMustBeString` | Error | `exec` 参数表达式的静态结果类型不是 `string`。 |
| `PEVT7404` | `ExecUsedAsExpression` | Error | `exec(...)` 被用于初始化器、赋值右侧、运算、调用参数或其他表达式位置。 |
| `PEVT7405` | `AsyncModifierOnExec` | Error | 使用 `async exec(...)`；动态片段执行语句本身不能声明为异步。 |
| `PEVT7406` | `ExecUsedAsHandlerInitializer` | Error | 使用 `handler 名称 = exec(...)`；`exec` 不产生异步句柄。 |

> 宿主事件的加载时静态分析只检查 `exec` 参数的数量、类型和使用位置，不把运行时字符串内容作为宿主源文件的一部分解析。

## 异步操作与句柄

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT7201` | `InvalidAsyncTarget` | Error | `async` 被用于 `@` 内置事件语句定义和 `block _` 自定义事件块定义以外的位置。 |
| `PEVT7202` | `MissingAsyncSeparator` | Error | `async` 与后面的 `@` 或 `block` 之间没有空白字符。 |
| `PEVT7203` | `AsyncCallUsedAsOrdinaryValue` | Error | 异步调用产生的句柄被直接赋给普通变量、常量，或参与普通表达式。 |
| `PEVT7204` | `SynchronousCallAssignedToHandler` | Error | `handler` 声明的初始化器是静态已知为同步的 `@` 或 `_` 调用；`callevt` 的目标属性改由运行时检查。 |
| `PEVT7205` | `MissingHandlerName` | Error | `handler` 后没有句柄名称。 |
| `PEVT7206` | `MissingHandlerInitializer` | Error | `handler` 声明没有使用 `=` 和异步调用立即初始化。 |
| `PEVT7207` | `DuplicateHandlerDeclaration` | Error | 当前环境中句柄名称与已有变量、常量、参数或句柄重名。 |
| `PEVT7208` | `HandlerAssignment` | Error | 尝试对已经初始化的句柄重新赋值。 |
| `PEVT7209` | `HandlerUsedAsOrdinaryValue` | Error | 句柄被用于 `await`、`kill`、`status` 以外的表达式、运算、转换或调用参数。 |
| `PEVT7210` | `UndefinedHandler` | Error | `await`、`kill` 或 `status` 引用了当前环境中尚未定义的句柄。 |
| `PEVT7211` | `AwaitVoidUsedAsExpression` | Error | 对没有普通返回值的异步操作执行 `await`，并将其作为表达式使用。 |
| `PEVT7212` | `InvalidAwaitOperand` | Error | `await` 后的名称不是句柄。 |
| `PEVT7213` | `InvalidKillOperand` | Error | `kill` 后的名称不是句柄。 |
| `PEVT7214` | `InvalidStatusOperand` | Error | `status` 后的名称不是句柄。 |
| `PEVT7215` | `AsyncModifierOnCall` | Error | 在调用位置写出 `async @...`、`async _...` 或 `async callevt...`；自定义块定义必须写作 `async block _...`，事件使用文件头 `enable async`。 |
| `PEVT7216` | `InvalidAggregateAwaitMode` | Error | `await` 后使用了 `all`、`any` 以外的集合等待模式。 |
| `PEVT7217` | `MissingAggregateAwaitHandlers` | Error | `await all` 或 `await any` 没有提供非空句柄列表。 |
| `PEVT7218` | `InvalidAggregateAwaitHandler` | Error | 集合等待的句柄列表包含当前环境中未定义、未初始化或不是句柄的名称。 |
| `PEVT7219` | `DuplicateAggregateAwaitHandler` | Error | 同一个句柄在一次集合等待的输入列表中重复出现。 |
| `PEVT7220` | `MissingAggregateAwaitBindings` | Error | 集合等待的句柄列表后缺少必需的结果绑定括号。 |
| `PEVT7221` | `AggregateAwaitBindingCountMismatch` | Error | 非空结果绑定列表中的变量数量与句柄数量不同。 |
| `PEVT7222` | `InvalidAggregateAwaitBinding` | Error | 结果绑定列表中包含变量名称以外的内容或显式类型声明。 |
| `PEVT7223` | `DuplicateAggregateAwaitBinding` | Error | 结果绑定名称与当前环境中的已有名称或同一绑定列表中的其他名称重复。 |
| `PEVT7224` | `AggregateAwaitBindingRequiresReturn` | Error | 使用了非空结果绑定列表，但至少一个句柄对应的异步定义没有普通返回值，或该句柄来自不产生普通返回值的 `callevt`。 |
| `PEVT7225` | `AggregateAwaitBindingInNestedExpression` | Error | 带非空结果绑定列表的集合等待被嵌入运算、括号、调用参数或其他更大的表达式中。 |

## 原始调用

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT8001` | `MissingRawTarget` | Error | `$raw` 后没有调用目标。 |
| `PEVT8002` | `InvalidRawTarget` | Error | `$raw` 后的调用目标不是 `cmd` 或 `cs`。 |
| `PEVT8003` | `MissingRawBlock` | Error | `$raw cmd` 或 `$raw cs` 后没有原始文本块。 |
| `PEVT8004` | `UnterminatedRawBlock` | Error | 原始文本块没有以三个 ASCII 单引号 `'''` 闭合。 |
| `PEVT8005` | `UnexpectedRawArgument` | Error | 原始文本块结束后还有额外参数或 token。 |
| `PEVT8006` | `RawCallUsedAsExpression` | Error | `$raw cmd` 被用于表达式，或无返回值的 `$raw cs` 被用于表达式。 |
| `PEVT8007` | `InvalidRawCSharp` | Error | `$raw cs` 中的内容不是合法的 C# 语句。 |
| `PEVT8008` | `InvalidRawCSharpReturnType` | Error | `$raw cs` 返回了 `int`、`float`、`bool`、`char`、`string` 以外的类型。 |
| `PEVT8009` | `InconsistentRawCSharpReturnType` | Error | 同一个 `$raw cs` 代码块中的有值 `return` 返回了不同的 PEVT 类型。 |
| `PEVT8010` | `RawCSharpNotAllPathsReturn` | Error | `$raw cs` 作为表达式使用，但存在可达的 C# 退出路径没有返回值。 |
| `PEVT8011` | `InvalidRawCSharpArgumentList` | Error | `$raw cs` 的变量传入列表不符合 `(变量名, ...)` 语法。 |
| `PEVT8012` | `RawCSharpArgumentMustBeVariable` | Error | `$raw cs` 的变量传入列表中包含了表达式或其他非变量内容。 |
| `PEVT8013` | `DuplicateRawCSharpArgument` | Error | 同一个变量在一个 `$raw cs` 变量传入列表中重复出现。 |
| `PEVT8014` | `UndefinedRawCSharpArgument` | Error | `$raw cs` 的变量传入列表引用了尚未定义的 PEVT 变量。 |
| `PEVT8015` | `RawCSharpNotEnabled` | Error | 当前文件使用了 `$raw cs`，但没有在 `id` 后紧接着声明 `enable cs`。 |
| `PEVT8016` | `RawBlockMustBeAdjacent` | Error | 原始文本块开始分隔符 `'''` 没有紧贴左侧的 `cmd`、`cs` 或 C# 变量传入列表右括号 `)`。 |
| `PEVT8017` | `InvalidRawDelimiterEscape` | Error | 原始内容试图转义结束分隔符，但没有使用规定的 `\'''` 形式。 |

## 加载器与静态分析器内部错误

| 编号 | 名称 | 级别 | 触发条件 |
| --- | --- | --- | --- |
| `PEVT9001` | `InternalStaticAnalysisError` | Error | PEVT 加载、解析或静态分析发生内部异常，且错误不由合法的源语法诊断覆盖。 |

## 编号稳定性

- 已分配的编号不得改变含义。
- 删除诊断后不得复用其编号。
- 新诊断必须加入对应编号分区。
- 一处源位置可以产生多个诊断，但解析器应避免重复报告同一个根因。
- `Error` 阻止该事件进入可执行状态。
- `Warning` 不阻止事件完成加载并进入可执行状态。
