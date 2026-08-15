# PEVT 语法规范（草案）

## 1. 文件

- 文件扩展名为 `.pevt`。
- 源文件编码固定为 UTF-8。
- 文件可以带或不带 UTF-8 BOM；BOM 仅允许出现在文件起始位置，并且不作为语法 token。
- 不能解码为合法 UTF-8 的源文件不得进入词法分析。
- 一个文件只能声明一个事件。
- 文件的第一个语法语句必须是事件 ID 声明。

> 注：缩进不是语法要求，不影响语句的归属或执行结果。为了便于阅读，推荐每嵌套一层使用四个空格缩进。

### 1.1 执行模型

- PEVT 是由 PolarisEvent 托管的解释型事件语言，不是预编译为原版 `.cmd` 的编译型语言。
- PolarisEvent 加载 `.pevt` 源文件后，先完成词法、语法、类型、名称和静态控制流校验；存在静态错误的事件不会进入可执行状态。
- 加载时静态诊断使用 `PEVTxxxx` 编号，规则见 `PEVT-静态诊断表.md`。
- 静态校验通过后，PolarisEvent 保留该事件的 PEVT 源信息或内部语法表示，并在事件运行时按当前执行位置动态解释语句。
- 内部语法表示只用于解释器执行和诊断，不是 `.cmd`、原版 Ev 脚本、可分发二进制或另一种公开语言。
- PEVT 不生成、缓存或回写等价的原版 `.cmd` 文件，也不要求存在一份与当前事件对应的原版 Ev 事件。
- 流程控制、表达式、变量、常量、自定义事件块、异步、句柄、`callevt` 和 `@` 调用全部由 PolarisEvent 内置 PEVT 解释器直接解释和执行。
- 除 `$raw cmd` 外，PEVT 语句不得序列化为原版 Ev 命令，也不得交给原版 Ev 读取器重新解析。
- `$raw cmd` 是唯一允许把文本提交给原版 Ev 命令解析通道的 PEVT 语法；它只执行其原始文本块，不改变整个事件的解释型执行模型。
- `$raw cs` 由 PolarisEvent 的受控 C# 执行通道处理，同样不会把当前 PEVT 事件转换为 `.cmd`。
- `callevt` 创建或进入另一个 PolarisEvent PEVT 解释实例，不切换到原版 Ev 读取器。
- `@` 由 PolarisEvent 直接分派到已登记处理器；处理器可以使用受控 C# 服务实现，但不应通过生成 Ev 命令文本来重新包装原版 DSL。

### 1.2 语句边界

- 除 `$raw cmd`、`$raw cs` 的原始文本块和第 8.9 节规定的多行字符串字面量外，每条语法语句必须完整写在同一个物理行内。
- 普通语句以换行符或文件末尾结束，不使用分号作为结束符。
- 同一物理行内最多只能书写一条普通语句。
- 普通表达式、参数列表、定义签名和流程控制行均不能跨行续写；多行字符串只允许其自身按固定形式跨行，不能顺带让其他表达式结构自由续行。
- 空行会被忽略。
- 行首和行尾的空白字符不属于语句内容。
- `$raw` 语句可以通过 `'''` 原始文本块跨越多个物理行；只有与其配对的结束分隔符出现后，该 `$raw` 语句才结束。

### 1.3 注释

```pevt
// 整行注释
@dialogue_show("line01") // 行尾注释

/* 单行块注释 */

/*
跨越多个物理行的
块注释
*/
```

- `//` 开始单行注释，注释持续到当前物理行结束。
- `//` 可以独占一行，也可以写在一条完整普通语句之后。
- `/*` 开始块注释，遇到其后的第一个 `*/` 时结束。
- 块注释可以位于单个物理行内，也可以跨越任意多个物理行；空块注释 `/**/` 合法。
- 块注释不允许嵌套；块注释内部再次出现的 `/*` 只视为普通注释内容，仍由其后的第一个 `*/` 结束整个块注释。
- 块注释必须显式闭合；到达文件末尾仍未出现 `*/` 是静态错误。
- 注释内容不参与词法、语法和控制流分析。
- 字符串字面量、字符字面量及 `$raw` 原始文本块内部的 `//`、`/*` 和 `*/` 是普通内容，不开始或结束注释。
- 块注释中的 `//` 不开始单行注释；单行注释中的 `/*` 和 `*/` 也没有块注释意义。
- 注释在词法上按空白处理，但其中包含的物理换行仍然存在，不能使用跨行块注释把一条普通语句续写到后续物理行。
- 注释不能插入一个 token、表达式、参数列表或定义签名的中间后再于下一行继续原语句。

## 2. 事件 ID

语法：

```pevt
id "事件ID"
```

规则：

- `id` 声明必须存在。
- `id` 声明只能出现一次。
- `id` 必须是文件的第一个语法语句。
- 事件 ID 使用双引号字符串。
- 事件 ID 不能为空。
- 事件 ID 中只允许 ASCII 字母 `a-z`、`A-Z`、数字 `0-9` 和 Unicode 中文汉字；中文汉字按 Unicode `Unified_Ideograph` 属性识别。
- 事件 ID 不允许空格、路径分隔符、标点、下划线或其他字符。
- 事件 ID 中的双引号只用于包裹 ID，不属于 ID 内容。
- 事件 ID 区分大小写；例如 `Opening` 与 `opening` 是两个不同的事件 ID。
- `id` 是静态声明，不参与事件执行。

### 2.1 文件能力声明

语法：

```pevt
id "事件ID"
enable cs
enable async
```

规则：

- `enable` 是文件级能力声明，目前只允许 `enable cs` 和 `enable async`。
- 一个文件可以不声明能力，也可以声明其中一种或同时声明两种能力。
- 全部 `enable` 声明必须作为连续的语法语句紧跟在 `id` 之后；不同能力的声明顺序不限。
- 同一种能力在同一文件中最多声明一次。
- `enable` 声明不参与事件顺序执行，只作为 PEVT 解释器的文件元数据。
- `enable cs` 允许当前文件使用 `$raw cs`；不使用原始 C# 的文件应省略该能力。
- 未声明 `enable cs` 的文件不得在外层事件或任何自定义事件块中使用 `$raw cs`。
- `enable async` 将当前事件标记为异步事件；该标记只改变其他事件通过 `callevt` 调用当前事件时的调用方式。
- 未声明 `enable async` 的事件通过 `callevt` 被同步调用；声明后通过 `callevt` 被异步调用。
- `enable async` 不会自动把当前事件内部的 `@` 指令、自定义事件块或 `$raw` 调用改为异步操作。
- 当前只接受精确的全小写能力名称 `cs` 和 `async`，不接受额外参数。

## 3. 事件结束

语法：

```pevt
end
```

规则：

- `end` 不接受参数。
- `end` 终止当前执行路径。
- 每一条可达执行路径都必须以 `end` 终止。
- 不允许执行路径直接到达文件末尾。
- PEVT 解释器不得隐式补充 `end`。
- 对于没有分支的事件，最后一条可达语句必须是 `end`。

## 4. 条件语句

### 4.1 `if`

语法：

```pevt
if 表达式
    事件语句
endif
```

规则：

- `if` 后必须跟一个表达式。
- `if` 条件表达式的结果类型必须是 `bool`。
- `if` 开始一个条件语句。
- 每个 `if` 必须以 `endif` 结束。
- `if` 的语句体可以为空；为空时产生静态警告。

### 4.2 `elif`

语法：

```pevt
if 表达式
    事件语句
elif 表达式
    事件语句
endif
```

规则：

- `elif` 只能出现在尚未闭合的 `if` 中。
- `elif` 后必须跟一个表达式。
- `elif` 条件表达式的结果类型必须是 `bool`。
- 一个 `if` 可以包含零个或多个 `elif`。
- `elif` 不能独立出现。
- `elif` 的语句体可以为空；为空时产生静态警告。

### 4.3 `else`

语法：

```pevt
if 表达式
    事件语句
else
    事件语句
endif
```

规则：

- `else` 只能出现在尚未闭合的 `if` 中。
- `else` 不接受表达式。
- 一个 `if` 最多包含一个 `else`。
- `else` 必须位于全部 `elif` 之后。
- `else` 不能独立出现。
- `else` 的语句体可以为空；为空时产生静态警告。

### 4.4 `endif`

语法：

```pevt
endif
```

规则：

- `endif` 闭合当前 `if` 条件语句。
- 每个 `if` 必须有且只有一个对应的 `endif`。
- `endif` 不接受参数。
- `endif` 不能独立出现。

完整形式：

```pevt
if 表达式
    事件语句
elif 表达式
    事件语句
else
    事件语句
endif
```

## 5. 循环语句

语法：

```pevt
while 表达式
    事件语句
endwhile
```

规则：

- `while` 后必须跟一个表达式。
- `while` 条件表达式的结果类型必须是 `bool`。
- `while` 开始一个循环语句。
- 每个 `while` 必须以 `endwhile` 结束。
- `endwhile` 不接受参数。
- `endwhile` 不能独立出现。
- `while` 的语句体可以为空；为空时产生静态警告。

## 6. 选择语句

### 6.1 `switch`

语法：

```pevt
switch 表达式
    case 表达式
        事件语句
endswitch
```

规则：

- `switch` 后必须跟一个表达式。
- `switch` 必须以 `endswitch` 闭合。
- `switch` 后的第一条语句必须是 `case` 或 `default`。
- `switch` 中必须至少包含一个 `case` 或 `default`。
- `switch` 主表达式在进入选择语句时只求值一次，并保存本次求值的快照用于全部比较。
- `switch` 表达式依次与各个 `case` 表达式进行 `==` 比较。
- 没有 `case` 命中时执行 `default`；没有 `default` 时直接转移到 `endswitch` 之后。

### 6.2 `case`

语法：

```pevt
switch 表达式
    case 表达式
        事件语句
    case 表达式
        事件语句
endswitch
```

规则：

- `case` 后必须跟一个表达式。
- `case` 只能使用无副作用表达式。
- 无副作用表达式只能包含字面常量、已初始化变量或常量、允许的显式转换、逻辑非 `!`、括号和二元运算符。
- `case` 中不允许使用 `@` 调用、`_` 调用、`$raw cs`、`await` 或 `status`。
- 各个 `case` 表达式按源码顺序求值并与已保存的 `switch` 快照比较。
- 同一个 `switch` 中，忽略空白后 token 序列完全相同的 `case` 表达式不能重复。
- 不同 `case` 表达式在运行时得到相同值时，源码顺序最靠前的 `case` 命中。
- `case` 只能出现在尚未闭合的 `switch` 中。
- `case` 不能独立出现。
- `case` 的语句体可以为空；为空时产生静态警告。
- 命中一个 `case` 后，只执行该 `case` 的事件语句。
- `case` 之间不会顺序贯穿。
- 一个 `case` 执行结束后，控制流直接转移到 `endswitch` 之后。

### 6.3 `default`

语法：

```pevt
switch 表达式
    case 表达式
        事件语句
    default
        事件语句
endswitch
```

规则：

- `default` 只能出现在尚未闭合的 `switch` 中。
- 一个 `switch` 最多包含一个 `default`。
- `default` 不接受表达式或参数。
- `default` 不能独立出现。
- `default` 的语句体可以为空；为空时产生静态警告。

### 6.4 `endswitch`

语法：

```pevt
endswitch
```

规则：

- `endswitch` 闭合当前 `switch` 选择语句。
- 每个 `switch` 必须有且只有一个对应的 `endswitch`。
- `endswitch` 不接受参数。
- `endswitch` 不能独立出现。

### 6.5 `switch` 内的 `goto`

语法：

```pevt
goto 表达式
```

规则：

- `goto 表达式` 只能在尚未闭合的 `switch` 中使用。
- `goto` 的表达式必须与当前 `switch` 中某个 `case` 的表达式完全相同。
- 匹配范围仅限当前 `switch` 的全部 `case`。
- 不扫描外层、内层或其他 `switch` 的 `case`。
- 匹配成功后，控制流直接转移到对应 `case` 的第一条事件语句。
- `goto 表达式` 不匹配 `default`。

## 7. 标签与跳转

### 7.1 标签

语法：

```pevt
#LabelName
```

规则：

- `#` 声明一个事件内标签。
- `#` 后必须跟一个标识符。
- 文件外层事件与每个自定义事件块分别拥有独立的标签环境。
- 同一标签环境内不能重复声明相同标签。
- `if`、`while` 和 `switch` 不创建新的标签环境。

### 7.2 标签 `goto`

语法：

```pevt
goto #LabelName
```

规则：

- `goto` 后必须跟一个标签引用。
- 标签引用使用 `#` 加标签标识符。
- `goto` 只能跳转到当前标签环境中的标签。
- 被引用的标签必须存在。
- `goto` 可以向前或向后跳转。
- 禁止从文件外层事件跳入自定义事件块，也禁止从自定义事件块跳到文件外层事件或其他自定义事件块。
- 每个标签和 `goto` 都按其所在的 `if`、`elif`、`else`、`while`、`case`、`default` 嵌套顺序记录一条结构路径。
- 目标标签的结构路径必须是 `goto` 来源结构路径的前缀。
- 因此，`goto` 可以停留在同一结构层级或从一层或多层结构中向外跳出，但不能从外部跳入结构内部，也不能跳入兄弟分支或其他不属于来源路径的结构。
- 允许从结构化流程语句内部跳到同一标签环境中位于其外部的标签。
- `goto` 不得绕过变量声明或初始化后再读取相应变量；跳转后的变量可用性仍按全部可达路径检查。

## 8. 表达式

### 8.1 表达式结构

- 表达式可以通过运算符与其他表达式组合成更大的表达式。
- 组合表达式可以拆解为一个或多个更小的表达式。
- 表达式的基本操作数可以是字面常量、当前环境中已经定义并初始化的变量或常量、允许的显式转换，以及规则允许产生值的调用或异步表达式。
- 尚未定义、尚未初始化或不属于当前环境的变量不能作为表达式使用。
- 表达式具有静态类型。
- 二元运算符左右两侧的表达式必须具有完全相同的类型。
- 不进行隐式类型转换。

### 8.2 变量类型

变量只能具有以下类型：

| 类型 | 定义 |
| --- | --- |
| `int` | 标准 32 位有符号整数。 |
| `float` | 标准单精度浮点数。 |
| `bool` | 布尔值。 |
| `char` | 单个字符。 |
| `string` | 标准字符串。 |

规则：

- 不允许声明或使用表中未定义的类型。
- `int` 与 `float` 是不同类型，二者之间不进行隐式转换。
- `char` 与 `string` 是不同类型，二者之间不进行隐式转换。
- 每个变量分别记录声明类型和初始化状态。
- 未初始化变量仍然具有声明时指定的类型。
- 未初始化变量可以作为赋值运算符 `=` 的左侧目标。
- 对变量完成一次有效赋值后，该变量进入已初始化状态。
- 未初始化变量不能被读取，也不能作为表达式的操作数。

### 8.3 显式类型转换

允许的转换：

```pevt
(float)int变量
(string)char变量
```

规则：

- `int` 可以通过 `(float)` 转换为 `float`。
- `char` 可以通过 `(string)` 转换为 `string`。
- 转换标记必须直接位于变量名前。
- 转换标记与变量名之间不能有空格或其他 token。
- 转换只能直接作用于一个已定义变量。
- 不允许对复合表达式使用转换标记。
- 不允许除 `int` 转 `float`、`char` 转 `string` 以外的类型转换。
- 转换表达式的类型是目标类型。
- 转换不会改变原变量的类型或值。

合法形式：

```pevt
(float)count
(string)letter
```

非法形式：

```pevt
(float) count
(string) letter
(int)value
(char)text
```

### 8.4 数学运算符

数学运算符：

```text
+  -  *  /  %
```

规则：

- 数学运算符是二元运算符。
- 左右两侧必须具有完全相同的类型。
- 数学运算符只接受 `int` 或 `float`。
- 运算结果与左右操作数具有相同类型。

#### 一元取负

```pevt
-1
-value
-(a + b)
```

- 当解析器处于“需要读取一个操作数”的位置并遇到 `-` 时，`-` 解释为一元取负。
- 一元取负只作用于紧随其后的一个操作数；该操作数的结果类型必须是 `int` 或 `float`。
- 一元取负的结果类型与操作数相同。
- 当 `-` 左侧已经存在一个完整操作数时，`-` 解释为二元减法。
- `-` 的含义只由语法位置决定；左侧操作数类型错误时仍按二元减法报告类型错误，不会重新解释为一元取负。
- 在二元运算符之后再次出现 `-` 时，解析器正在等待右操作数，因此该 `-` 是一元取负；例如 `a - -b` 等价于 `a - (-b)`。
- 对直接跟随一元负号的数字字面量进行范围检查时，以应用负号后的结果为准，因此 `-2147483648` 是合法 `int` 值。

### 8.5 赋值语句

赋值运算符：

```text
=
```

规则：

- `=` 左侧必须是一个已定义且可写的变量。
- `=` 右侧必须是一个表达式。
- 左右两侧必须具有完全相同的类型。
- 赋值不会执行隐式类型转换。
- 赋值只能作为一条独立事件语句使用，不产生表达式值。
- 赋值不能嵌入初始化器、条件、调用参数、运算表达式或其他赋值语句。

### 8.6 比较运算符

相等比较运算符：

```text
==  !=
```

规则：

- `==` 和 `!=` 是通用比较运算符。
- 左右两侧必须具有完全相同的类型。
- `==` 和 `!=` 可以用于 `int`、`float`、`bool`、`char` 或 `string`。
- 相等比较表达式的结果类型为 `bool`。

顺序比较运算符：

```text
<  <=  >=  >
```

规则：

- `<`、`<=`、`>=` 和 `>` 只能用于数字类型。
- 数字类型仅包括 `int` 和 `float`。
- 左右两侧必须具有完全相同的数字类型。
- `int` 与 `float` 直接比较无效；必须先显式转换成相同类型。
- 顺序比较表达式的结果类型为 `bool`。

### 8.7 逻辑运算符

逻辑运算符：

```text
&  |  ^  !
```

规则：

- `&`、`|` 和 `^` 是二元运算符。
- `&`、`|` 和 `^` 的左右两侧都必须是 `bool`。
- `&`、`|` 和 `^` 的结果类型为 `bool`。
- `!` 是一元运算符。
- `!` 只作用于紧随其后的一个操作数。
- `!` 的操作数必须是 `bool`。
- `!` 的结果类型为 `bool`。

### 8.8 运算顺序与括号

规则：

- 未使用括号包裹的链式表达式按照从左到右的线性顺序运算。
- 解析时先取得第一个操作数，再依次读取“二元运算符 + 下一个操作数”；每一步的结果成为下一步的左操作数。
- 不同二元运算符之间没有其他隐式优先级。
- 使用 `(` 和 `)` 包裹的表达式必须作为一个整体先完成运算。
- 括号表达式的结果类型与括号内表达式的结果类型相同。
- 括号可以嵌套。
- 空括号不是合法表达式。
- 每个左括号 `(` 必须有对应的右括号 `)`。

示例：

```pevt
a + b * c
```

等价于：

```pevt
(a + b) * c
```

以下表达式先计算括号中的内容：

```pevt
a + (b * c)
```

### 8.9 字面常量

#### 整数字面量

```pevt
0
42
-2147483648
```

- 整数字面量本身由十进制数字组成；前置 `-` 按一元取负规则解析。
- 不允许小数点、指数、类型后缀或数字分隔符。
- 值必须位于标准 32 位有符号整数范围 `-2147483648` 至 `2147483647` 内。
- 超出范围是静态错误。

#### 浮点数字面量

```pevt
0.0
3.5
-12.25
```

- 浮点数字面量本身由小数点前数字、小数点 `.` 和小数点后数字组成；前置 `-` 按一元取负规则解析。
- 小数点两侧都必须至少存在一个数字；`.5` 和 `1.` 均无效。
- 不允许指数、类型后缀或数字分隔符。
- 值按标准 IEEE 754 单精度浮点数解析。
- 超出单精度有限值范围是静态错误。

#### 字符串字面量

```pevt
"text"
"line\nnext"
```

- 字符串使用一对双引号 `"` 包裹，可以为空。
- 字符串不能直接跨越源文件行。
- 支持转义 `\\`、`\"`、`\n`、`\r`、`\t` 和 `\0`。
- 其他转义形式无效。

#### 多行字符串字面量

```pevt
var text : string = "第一行" +
                    "第二行" +
                    "第三行"
```

上述值等价于：

```pevt
"第一行\n第二行\n第三行"
```

也可以在参数位置使用：

```pevt
@dialogue("Alice", "第一句" +
                   "第二句" +
                   "第三句")
```

规则：

- 一个字符串字面量后以 `+` 作为该物理行最后一个语法 token 时，开始多行字符串续接。
- `+` 后只能存在空格，不能再出现表达式、分隔符或注释。
- 下一物理行的第一个非空白 token 必须是另一个字符串字面量。
- 每个续接字符串的开始双引号 `"` 必须与第一段被续接字符串的开始双引号位于完全相同的源文件列。
- 续接行在开始双引号前只能使用 ASCII 空格，不能使用制表符；列号从 `1` 开始计算。
- 除最后一段外，每一段都必须再次以行末 `+` 继续；不允许在各段之间插入空行或注释行。
- 最后一段字符串之后可以继续书写原语句尚未闭合的 `)`、`,` 或其他合法后续 token。
- 解释器先分别解析每段字符串的转义，再在相邻两段之间插入一个 Unicode LF 字符 `U+000A`；源文件使用 CRLF 或 LF 不影响结果。
- 多行字符串整体是一个 `string` 字面量操作数，不是多个二元 `+` 运算。
- 词法分析优先识别本节的“字符串 + 物理换行”形式；同一物理行中的 `"a" + "b"` 不属于该语法糖，并继续按普通二元运算规则处理。
- 此例外只允许字符串内容跨行，不允许变量、普通运算或调用签名借助 `+` 任意跨行。

#### 字符字面量

```pevt
'A'
'\n'
```

- 字符使用一对单引号 `'` 包裹。
- PEVT 的 `char` 与 C# `System.Char` 对应，表示恰好一个 UTF-16 代码单元。
- 完成转义解析并转换为 UTF-16 后必须恰好包含一个代码单元。
- 需要一对 UTF-16 代理项表示的非 BMP Unicode 字符不能作为 `char`，但可以出现在 `string` 中。
- 空字符、多个字符和未闭合字符均无效。
- 支持转义 `\\`、`\'`、`\n`、`\r`、`\t` 和 `\0`。

#### 布尔字面量

```pevt
true
false
```

- 布尔字面量只能是全小写的 `true` 或 `false`。
- 其他大小写或数值形式均无效。

## 9. 变量与常量

### 9.1 变量声明

未初始化变量：

```pevt
var xx : int
```

声明时初始化：

```pevt
var xx : int = 1
```

声明后初始化：

```pevt
var xx : int
xx = 1
```

规则：

- 变量使用 `var` 声明。
- `var` 后必须跟变量名。
- 变量名后必须使用 `:` 显式指定类型。
- 类型必须后置，不能写在变量名前。
- 变量类型必须是 `int`、`float`、`bool`、`char` 或 `string`。
- 变量声明可以不带初始化器。
- 未带初始化器的变量处于未初始化状态。
- 变量声明可以使用 `=` 和一个同类型表达式立即初始化。
- 初始化表达式的类型必须与显式声明的变量类型完全相同。
- 变量可以在声明后的其他语句中通过 `=` 初始化或重新赋值。

### 9.2 常量声明

语法：

```pevt
const xx : int = 1
```

规则：

- 常量使用 `const` 声明。
- `const` 后必须跟常量名。
- 常量名后必须使用 `:` 显式指定类型。
- 类型必须后置，不能写在常量名前。
- 常量类型必须是 `int`、`float`、`bool`、`char` 或 `string`。
- 常量必须在声明语句中初始化。
- 常量初始化器可以是显式常量或其他表达式。
- 初始化表达式的类型必须与显式声明的常量类型完全相同。
- 常量完成初始化后不能再次赋值。
- 常量可以作为表达式中的已定义变量使用。

### 9.3 快照值

- 变量或常量使用表达式初始化时，在初始化语句执行时对表达式求值一次。
- 初始化目标保存本次求值结果的快照值。
- 初始化目标不与原表达式或表达式引用的变量建立动态绑定。
- 原表达式引用的变量之后发生变化，不会自动改变已经保存的快照值。
- 普通赋值语句同样在赋值语句执行时求值一次，并保存本次结果的快照值。

示例：

```pevt
var source : int = 1
const snapshot : int = source
source = 2
```

执行后：

```text
source   = 2
snapshot = 1
```

### 9.4 环境、可见性与使用顺序

- 文件的外层事件拥有一个外层环境。
- 每个自定义事件块拥有一个独立的局部环境。
- 每次调用自定义事件块时，都会为该次调用创建一个新的局部环境。
- 自定义事件块不会隐式捕获外层环境中的变量或常量。
- 自定义事件块只能访问自己的参数，以及在该事件块内声明的变量和常量。
- 外层事件不能访问自定义事件块内部声明的参数、变量或常量。
- 不同环境可以声明同名变量或常量，二者互不关联。
- `if`、`elif`、`else`、`while`、`switch` 和 `case` 不另外创建变量环境。
- 名称解析只在当前环境中进行，不自动向其他环境查找。
- 变量或常量的声明必须出现在第一次使用之前。
- 变量必须在读取、参与表达式、作为调用实参或作为 `return` 目标之前完成赋值。
- 常量在声明时完成初始化，因此声明完成后即可使用。
- 一个名称只有在到达使用位置的每条可达路径上都已经执行声明，才视为已经定义。
- 一个变量只有在到达使用位置的每条可达路径上都已经完成赋值，才视为已经初始化。
- 仅在某个条件分支中声明或赋值，不能使其在其他未经过该分支的路径上变为已定义或已初始化。
- `while` 循环体可能一次也不执行，因此仅在循环体内完成的声明或赋值不能保证变量在循环之后可用。
- 调用自定义事件块时，实参表达式在调用者的当前环境中求值；进入事件块后，形参在事件块的局部环境中视为已经定义并初始化。

### 9.5 标识符

普通标识符格式：

```text
[A-Za-z_][A-Za-z0-9_]*
```

调用名称主体格式：

```text
[A-Za-z][A-Za-z0-9_]*
```

规则：

- 变量、常量、参数、句柄、标签和集合等待结果变量使用普通标识符。
- 普通标识符首字符只能是 ASCII 字母或下划线 `_`，其余字符只能是 ASCII 字母、数字或下划线。
- `@` 内置事件语句名称使用调用名称主体格式，并由 `@` 作为前缀。
- 自定义事件块名称使用调用名称主体格式，并由 `_` 作为固定前缀；因此完整名称形如 `_play_scene`，不能形如 `__play_scene`。
- 所有标识符、标签、内置事件语句名称和自定义事件块名称均区分大小写。
- 事件 ID 使用第 2 节的独立字符规则，不属于标识符。

### 9.6 保留关键字

以下名称是 PEVT 保留关键字：

```text
id enable cs cmd end block
callevt
if elif else endif
while endwhile
switch case default endswitch
goto
var const
int float bool char string
true false
return endblock
async handler await all any kill status
exec
```

规则：

- 保留关键字不能用作变量、常量、参数、句柄、标签、调用名称主体或 `await all`、`await any` 结果绑定变量的名称。
- 关键字按全小写形式保留。
- `@` 内置事件语句名称和带 `_` 前缀的自定义事件块名称不按普通变量名称解析。

## 10. 事件间调用

### 10.1 基本语法

```pevt
callevt "OtherEvent"
```

规则：

- `callevt` 用于按事件 ID 直接调用另一个 PEVT 事件。
- `callevt` 后必须跟一个事件 ID 字面量，不接受变量、普通字符串表达式或其他动态表达式。
- 目标字面量使用第 2 节规定的事件 ID 字符规则，并且区分大小写。
- PEVT 加载器只检查目标 ID 字面量的语法，不查找当前工程、模组或文件系统中是否实际存在对应事件。
- 目标事件由 PolarisEvent 在执行到 `callevt` 时通过当前运行时事件注册表解析，因此允许调用其他模组在运行时注册的事件。
- `callevt` 不接受参数，也不产生普通 `int`、`float`、`bool`、`char` 或 `string` 返回值。
- `callevt` 不能作为普通表达式、普通变量初始化器、常量初始化器、赋值右侧或调用参数使用。
- 调用位置不书写 `async callevt`；目标事件是否异步由其自身的 `enable async` 标记决定。

### 10.2 同步事件调用

- 目标事件没有声明 `enable async` 时，`callevt` 创建并执行一个同步子事件调用。
- 调用者暂停在 `callevt` 语句处，直到目标事件执行到 `end` 或异常终止。
- 目标事件正常执行到 `end` 后，调用者从 `callevt` 的下一条语句继续执行。
- 同步目标不能用于 `handler 名称 = callevt "ID"`；由于目标属性只在运行时解析，该错误由 PolarisEvent 在运行时报告。
- 同步目标发生未处理运行时异常时，异常沿事件调用关系传播到调用者。

### 10.3 异步事件调用

独立调用并放弃句柄：

```pevt
callevt "AsyncEvent"
```

保存句柄：

```pevt
handler evt = callevt "AsyncEvent"
```

规则：

- 目标事件声明了 `enable async` 时，`callevt` 创建一个由 PolarisEvent 管理的异步子事件执行实例。
- 独立调用时，调用者不等待目标事件，并在启动成功后立即继续；产生的句柄被放弃但仍由当前事件跟踪。
- `handler 名称 = callevt "ID"` 明确要求目标事件具有 `enable async` 标记，并保存本次调用产生的句柄。
- 静态校验允许 `callevt` 作为 `handler` 初始化器，但不验证目标事件是否异步；目标不存在或没有 `enable async` 均在运行时报告。
- 异步事件调用产生的句柄没有普通返回值，因此单句柄 `await` 只能作为事件语句使用。
- 该句柄可以用于 `await`、`kill`、`status`、`await all` 和 `await any`。
- 集合等待包含异步事件句柄时，只能使用空结果绑定列表；为该句柄提供结果变量属于静态错误。
- 异步目标执行到 `end` 时句柄正常完成；目标异常结束或被 `kill` 时句柄异常结束。
- 当前调用者事件结束时，其启动且仍未完成的异步事件调用遵守统一的自动清理规则。

### 10.4 运行时解析

- PolarisEvent 必须在每次执行 `callevt` 时按完整、区分大小写的事件 ID 查询运行时注册表。
- 没有找到目标 ID 是运行时异常，不是静态错误。
- 如果多个已加载模组注册了同一个事件 ID，且运行时没有唯一的覆盖或优先级结果，则调用因目标不明确而产生运行时异常。
- 目标在当前事件加载后被移除、模组尚未加载或事件注册表发生变化，均由调用时解析结果决定。

## 11. 内置事件语句

### 11.1 调用语法

语法：

```pevt
@语句名(参数表达式, 参数表达式)
```

规则：

- 所有以 `@` 开头的调用都被识别为内置事件语句。
- `@` 后必须紧跟内置事件语句名称。
- 内置事件语句名称后必须跟参数列表。
- 参数列表使用 `(` 和 `)` 包裹。
- 多个参数使用 `,` 分隔。
- 参数可以是任意类型正确且已经完成求值的表达式。
- `.pevt` 文件不能声明或覆盖内置事件语句。
- 未登记在内置事件语句 API 表中的 `@` 语句无效。
- 执行到 `@` 调用时，由 PolarisEvent 按登记名称和签名直接解析并分派到对应的内置处理器。
- `@` 不转换为原版游戏 DSL，也不经过另一套 PEVT 中间指令；PolarisEvent 在运行时直接调用其处理器。

无参数调用：

```pevt
@语句名()
```

### 11.2 API 签名

无返回值 API：

```pevt
@语句名(参数名 : 参数类型)
```

有返回值 API：

```pevt
@语句名(参数名 : 参数类型) : 返回值类型
```

异步 API：

```pevt
async @语句名(参数名 : 参数类型) : 返回值类型
```

规则：

- 每个内置事件语句必须在 API 表中登记名称、参数签名和作用。
- API 表中的签名是宿主侧注册契约，不是在 `.pevt` 文件中编写的自举定义语法。
- PEVT 不是自举语言；`.pevt` 源文件不能实现新的 `@` 处理器。
- `@` 处理器通常直接由 C# 实现，并调用 PolarisEvent 允许的受控游戏服务；除 `$raw cmd` 外不得把处理器参数拼装成原版 Ev 文本再交给 Ev 读取器。
- 处理器的内部实现对 PEVT 语法不可见；只要满足已登记的同步性、返回值、副作用、取消和异常契约即可。
- API 参数必须显式声明名称和类型。
- API 参数类型使用后置形式。
- API 没有书写返回值类型时，该 API 是纯调用。
- 纯调用只执行对应事件操作，不产生表达式值。
- 同步 API 书写返回值类型时，调用结果具有该返回值类型。
- 异步 API 无论是否书写普通返回值类型，调用结果都固定为 `handler`；其普通返回值由句柄在完成后保存。
- API 签名前可以使用 `async` 和至少一个空白字符，将该 API 声明为异步操作。
- 未使用 `async` 的 API 是同步操作。
- 参数类型和返回值类型只能是 `int`、`float`、`bool`、`char` 或 `string`。

### 11.3 参数匹配

- 调用时必须存在名称和参数签名完全匹配的 API。
- 实参数量必须与 API 形参数量完全相同。
- 实参按照声明顺序与形参逐一匹配。
- 每个实参表达式的结果类型必须与对应形参类型完全相同。
- 参数匹配不进行隐式类型转换。
- 允许使用显式转换表达式后再参与参数匹配。
- 参数缺失、参数多余或任一参数类型不匹配时，内置事件语句不得执行。
- 签名匹配必须在事件加载和静态校验时完成；匹配失败时该事件不得进入可执行状态。

示例 API：

```pevt
@perform(duration : int)
@query(name : string) : bool
```

对应调用：

```pevt
@perform(duration)
var result : bool = @query(name)
```

### 11.4 返回值

- 有返回值的同步内置事件语句可以作为普通表达式使用。
- 异步内置事件语句的调用只产生 `handler`，不能直接作为普通返回值表达式使用。
- 返回值类型由 API 签名唯一确定。
- 返回值参与初始化、赋值或其他表达式时，仍遵守严格类型匹配规则。
- 无返回值的同步内置事件语句不能作为表达式使用。
- 内置事件语句的返回值被赋给变量或常量时，保存调用完成时返回值的快照。

### 11.5 API 表

所有内置事件语句及其作用记录在独立文件：

```text
PEVT-内置事件语句表.md
```

## 12. 原始调用

### 12.1 原始游戏 DSL

单行语法：

```pevt
$raw cmd'''原始游戏 DSL'''
```

多行语法：

```pevt
$raw cmd'''
原始游戏 DSL 第一行
原始游戏 DSL 第二行
'''
```

规则：

- `$raw cmd` 表示原始游戏 DSL 调用。
- `cmd` 后必须紧接原始文本块的开始分隔符 `'''`，中间不能有空格或其他字符。
- 原始文本块中的游戏 DSL 在 `$raw cmd` 所在位置立即调用。
- 多行原始游戏 DSL 按原有顺序执行。

### 12.2 原始 C#

单行语法：

```pevt
$raw cs'''var a = 1;'''
```

传入变量：

```pevt
$raw cs (count, name)'''
count += 1;
var text = name;
'''
```

多行语法：

```pevt
$raw cs'''
var a = 1;
a += 1;
'''
```

规则：

- `$raw cs` 表示原始 C# 调用。
- 当前文件必须已经在 `id` 后紧接着声明 `enable cs`。
- `cs` 后可以有一个变量传入列表，随后必须跟一个原始文本块。
- 没有变量传入列表时，开始分隔符 `'''` 必须紧贴 `cs` 左侧语法部分，写作 `cs'''`。
- 存在变量传入列表时，开始分隔符 `'''` 必须紧贴参数列表的右括号 `)`，写作 `)'''`。
- 变量传入列表使用 `(` 和 `)` 包裹，各变量名之间使用逗号 `,` 分隔。
- 变量传入列表至少包含一个变量；不传入变量时应省略整个列表。
- 变量传入列表只能包含已定义且已初始化的 PEVT 变量，不能包含表达式。
- 每个传入变量都会在 C# 侧创建一个同名、同类型的局部副本。
- 传入值是执行 `$raw cs` 时取得的快照。
- C# 侧对局部副本的修改不会反写原 PEVT 变量。
- 同一个变量不能在同一传入列表中重复出现。
- 未写变量传入列表时，不向 C# 侧传入任何 PEVT 变量。
- 原始文本块中的 C# 语句在 `$raw cs` 所在位置立即调用。
- 原始文本块必须包含合法的 C# 语句。
- 原始 C# 可以使用 `return;` 立即结束当前原始 C# 代码块。
- 原始 C# 可以使用 `return 值;` 立即结束当前原始 C# 代码块并向 PEVT 返回一个值。
- 原始 C# 代码块中完全没有 `return` 时，视为在代码块末尾存在一个隐式 `return;`。

有返回值示例：

```pevt
var result : int = $raw cs'''
var a = 1;
return a;
'''
```

### 12.3 原始 C# 返回值

允许返回的 C# 类型：

| C# 类型 | PEVT 类型 |
| --- | --- |
| `System.Int32` / `int` | `int` |
| `System.Single` / `float` | `float` |
| `System.Boolean` / `bool` | `bool` |
| `System.Char` / `char` | `char` |
| `System.String` / `string` | `string` |

规则：

- `$raw cs` 只能向 PEVT 返回表中列出的类型。
- 返回其他 C# 类型是静态错误。
- `return 值;` 返回值的 C# 静态类型决定 `$raw cs` 表达式的 PEVT 类型。
- 同一个原始 C# 代码块中的全部有值 `return` 必须返回同一种 PEVT 类型。
- `$raw cs` 作为 PEVT 表达式使用时，每一条可达的 C# 退出路径都必须返回一个值。
- `$raw cs` 完全没有 `return` 时，隐式补充的 `return;` 不产生返回值。
- 没有返回值的 `$raw cs` 是纯调用，不能作为 PEVT 表达式使用。
- `$raw cs` 的返回值被赋给变量或常量时，保存返回时的快照值。
- `return` 只退出当前 `$raw cs` 代码块，不直接结束外层 PEVT 事件。

### 12.4 原始文本块

语法：

```text
'''原始内容'''
```

规则：

- 原始文本块使用三个连续的 ASCII 单引号 `'''` 开始。
- 原始文本块使用三个连续的 ASCII 单引号 `'''` 结束。
- 开始分隔符必须紧贴其左侧的 `cmd`、`cs` 或 C# 变量传入列表右括号 `)`，中间不能包含空白。
- 开始和结束分隔符之间可以包含换行。
- 开始和结束分隔符不属于原始内容。
- 分隔符之间的内容按原文保留。
- 原始内容中的 `\'''` 表示字面内容 `'''`，不会结束原始文本块；解释器提交原始内容前移除用于转义的反斜杠。
- 除 `\'''` 外，反斜杠在原始文本块中不具有 PEVT 转义意义并按原文保留。
- PEVT 解释器不把原始内容解析为 PEVT 语法。
- PEVT 变量和常量不会在原始内容中自动替换。
- `$raw cmd` 不产生 PEVT 返回值，不能作为表达式使用。
- `$raw cs` 仅在所有可达退出路径返回合法值时可以作为表达式使用。
- `$raw` 后只允许 `cmd` 或 `cs`。
- `$raw cmd` 和 `$raw cs` 在控制流分析中各自视为一条普通事件语句。
- 原始内容中的跳转、分支或结束操作不参与 PEVT 控制流分析。
- 即使原始内容中包含游戏 DSL 的结束指令，PEVT 的可达路径仍必须显式以 `end` 终止。

## 13. 动态 PEVT 执行

### 13.1 语法

```pevt
exec(source)
```

直接执行多行 PEVT 片段：

```pevt
exec("@dialogue(\"Alice\", \"欢迎回来。\")" +
     "@sound_play(\"door_open\")")
```

规则：

- `exec` 是由 PEVT 解释器实现的特殊执行语句，不是可注册或覆盖的 `@` 内置事件语句。
- `exec` 必须且只能接收一个结果类型为 `string` 的表达式。
- 参数表达式在执行到 `exec` 时求值一次；解释器保存该结果的快照并将其作为 PEVT 片段源文本。
- `exec` 同步解析并执行片段；片段正常结束后，外层流程从 `exec` 的下一条语句继续。
- `exec` 不产生普通返回值，只能作为独立事件语句使用。
- 不允许写成 `async exec(...)`，也不能作为 `handler` 初始化器。
- `exec` 解释的是 PEVT 片段，不是原版 Ev、`.cmd` 或 C#；它不会把文本提交给原版 Ev 读取器。
- 即使参数在源文件中是常量字符串，外层事件的加载阶段也只检查参数类型，不把字符串内容当作外层源码提前解析。

### 13.2 片段结构

- 动态片段不包含 `id`，不需要也不允许以 `end` 结束。
- 片段可以包含 `@` 调用、已经在宿主源码中完成定义的 `_` 自定义事件块调用、`callevt`、变量或常量声明、赋值、`if/elif/else/endif`、`while/endwhile`、`switch/case/default/endswitch`、异步调用、句柄操作、`$raw` 和嵌套 `exec`。
- 片段中的结构化流程语句必须完全在当前片段内闭合。
- 动态片段禁止出现 `id`、`enable`、`block`、`endblock`、`return`、`end`、标签声明和任何形式的 `goto`。
- 禁止动态定义自定义事件块，也禁止从动态片段跳入或跳出外层控制流。
- 片段执行到自身文本末尾时视为正常完成，不会终止外层事件。
- 片段中的 `$raw cs` 只有在宿主 `.pevt` 文件声明了 `enable cs` 时才允许执行；动态片段不能自行添加或扩大文件能力。
- 片段中的 `$raw cmd` 仍是唯一能够进入原版 Ev 读取器的路径。

### 13.3 环境

- `exec` 在调用位置的当前环境之上创建一个临时片段环境。
- 动态片段可以读取当前环境中已经定义并初始化的变量、常量、参数和句柄。
- 对外层已经声明的可写变量进行赋值时，修改直接作用于该外层变量。
- 动态片段内部声明的变量、常量和句柄只在当前片段及其嵌套结构中可见；片段结束后全部离开作用域。
- 动态片段中的声明不能使同名名称在外层源码中变为静态可见。
- 动态片段对外层未初始化变量的赋值，不会改变外层加载阶段的确定赋值结论；外层源码仍不能依赖 `exec` 保证变量已经初始化。
- 动态片段启动的异步操作归属于当前 PolarisEvent 执行实例；片段局部句柄离开作用域后，未完成操作仍由该事件统一跟踪和清理。

### 13.4 运行时校验与失败

- PolarisEvent 在每次执行 `exec` 时，对片段进行词法、语法、类型、名称、能力和受限控制流校验。
- 片段校验失败是运行时异常，不会回溯成为宿主文件的加载时静态错误。
- 片段内普通语句产生的运行时异常按原诊断编号向外传播，并终止当前 `exec` 和外层同步流程。
- 允许嵌套调用 `exec`，但 PolarisEvent 必须限制动态执行嵌套深度，并将其计入当前事件的统一执行预算。
- `exec` 不绕过 `@` 注册表、普通类型规则、`enable cs`、异步所有权、运行诊断或事件清理规则。
- 宿主事件的静态控制流把整个 `exec` 视为一条可能正常继续的普通语句，不展开动态片段中的分支，也不采信片段中的声明或赋值来完成宿主确定赋值分析。

## 14. 自定义事件块

### 14.1 定义语法

无参数、无返回值：

```pevt
block _playOpening()
    @语句名()
endblock
```

带参数、无返回值：

```pevt
block _playLine(name : string, duration : int)
    @语句名(name, duration)
endblock
```

带参数和返回值：

```pevt
block _selectLine(name : string) : bool
    var selected : bool = false
    @语句名(name)
    return selected
endblock
```

异步事件块：

```pevt
async block _loadScene(name : string) : bool
    var loaded : bool = @语句名(name)
    return loaded
endblock
```

规则：

- 自定义事件块的完整名称必须以 `_` 开头。
- `_` 后必须紧跟事件块名称。
- 自定义事件块定义必须以关键字 `block` 开始。
- 自定义事件块的定义签名为 `block _名称(参数名 : 参数类型, ...)`。
- `block` 用于明确区分定义与 `_名称(...)` 调用。
- 自定义事件块可以不声明参数，空参数列表写作 `()`。
- 参数必须显式使用后置类型，允许的类型只有 `int`、`float`、`bool`、`char`、`string`。
- 参数在事件块内是已经初始化的同名局部变量，其值为调用时实参结果的快照。
- 返回值类型写在参数列表之后，语法为 `: 返回值类型`。
- `block` 前可以使用 `async` 和至少一个空白字符，将该自定义事件块声明为异步操作，完整形式为 `async block _名称(...)`。
- 未使用 `async` 的自定义事件块是同步操作。
- 未声明返回值类型的事件块没有返回值。
- 同名自定义事件块只能定义一次，不允许重载。
- 自定义事件块必须在第一次调用之前完成定义，即静态名称解析必须已经读取到与其配对的 `endblock`。
- 不允许对尚未完成定义的自定义事件块进行前向调用。
- 自定义事件块在自身的 `endblock` 之前仍未完成定义，因此不能直接递归调用自身。
- 自定义事件块不能嵌套定义。
- 自定义事件块定义本身不由外层事件顺序执行；外层事件只在调用该块时进入块体。

### 14.2 块边界

- 自定义事件块从 `block` 或 `async block` 定义签名开始。
- 自定义事件块必须使用 `endblock` 显式闭合。
- 与定义签名配对的 `endblock` 属于该事件块，并结束该事件块的定义。
- `return` 只结束当前一次事件块调用，不结束事件块的语法定义。
- `endblock` 之后的事件代码不属于该事件块。
- `endblock` 不能单独存在，也不能用于闭合其他流程语句。
- 块边界不依赖缩进。

示例：

```pevt
block _playOpening()
    @语句名()
endblock

@另一条语句()
end
```

`@另一条语句()` 和 `end` 属于文件的外层事件，不属于 `_playOpening`。

### 14.3 `return`

- 执行到 `return` 时，立即结束当前自定义事件块并返回调用位置。
- 未声明返回值类型的事件块只能使用无返回值的 `return`。
- 声明了返回值类型的事件块必须在每个 `return` 后提供一个变量名或常量名。
- 声明了返回值类型的事件块，其每一条可达执行路径都必须执行一个有返回值的 `return`。
- 声明了返回值类型的事件块不能以任何可达路径直接运行到 `endblock`。
- 未声明返回值类型的事件块可以完全省略 `return`。
- 未声明返回值类型的事件块运行到 `endblock` 时，视为在该路径执行一个隐式的无返回值 `return`。
- 返回目标必须是已经定义且已经初始化的变量或常量。
- 返回目标的类型必须与事件块声明的返回值类型完全相同。
- `return` 后不允许直接书写字面量、运算表达式或调用表达式。
- 自定义事件块内部不允许使用 `end`；出现时是静态错误。
- 位于 `$raw cs` 原始文本块内部的 C# `return` 不属于本节语法。

### 14.4 调用语法

```pevt
_事件块名(参数表达式, 参数表达式)
```

规则：

- 调用自定义事件块时必须书写包含 `_` 的完整名称。
- 调用位置必须位于对应自定义事件块定义的 `endblock` 之后。
- 参数列表与内置事件语句调用相同，可以包含任意类型正确且已经完成求值的表达式。
- 实参数量、顺序和类型必须与自定义事件块的定义签名完全匹配。
- 参数匹配不进行隐式类型转换。
- 无返回值事件块只能作为事件语句调用。
- 同步且有返回值的事件块可以作为表达式调用，表达式类型为其声明的返回值类型。
- 异步事件块的调用立即返回 `handler`，不直接返回定义签名中声明的返回值。
- 调用返回值被赋给变量或常量时，保存返回时的快照值。

调用示例：

```pevt
_playLine(name, duration)
var selected : bool = _selectLine(name)
```

## 15. 异步操作

### 15.1 异步定义与调用

- 内置事件语句、自定义事件块和完整 PEVT 事件可以声明为异步操作。
- 异步内置事件语句的定义以 `async @` 开始。
- 异步自定义事件块的定义以 `async block _` 开始。
- 完整事件通过文件头的 `enable async` 声明为可由 `callevt` 异步调用的事件。
- `async` 与后面的 `@` 或 `block` 之间必须至少存在一个空白字符。
- `async` 不能修饰变量、常量、原始调用或其他流程语句。
- 调用语法不重复书写 `async`；调用方式仍为 `@名称(...)`、`_名称(...)` 或 `callevt "事件ID"`。
- 调用异步操作后，当前流程不等待异步操作完成，并立即继续执行下一条语句。
- 每次异步调用固定产生一个新的 `handler` 包装器。
- 异步调用可以作为独立事件语句使用；此时产生的句柄被丢弃，异步操作仍继续执行。
- 异步定义中没有声明返回值时，句柄只记录运行状态，不包含普通返回值。
- 异步定义中声明了返回值时，异步操作成功完成后，句柄自动保存该返回值。

### 15.2 `handler`

声明并保存句柄：

```pevt
handler a = @异步语句()
handler b = _异步事件块()
handler c = callevt "异步事件ID"
```

规则：

- `handler` 是异步句柄类别，不属于 `int`、`float`、`bool`、`char`、`string` 五种普通变量类型。
- 句柄使用 `handler 名称 = 异步调用` 声明，并且必须在声明时初始化。
- 初始化器只能是一个已声明为异步的 `@` 调用、`_` 调用，或一个在运行时要求目标带有 `enable async` 标记的 `callevt` 调用。
- 句柄的环境、可见性和使用顺序与普通变量一致。
- 同一个环境内，句柄名称不能与变量、常量、参数或其他句柄重名。
- 句柄不可重新赋值，也不能声明为 `var` 或 `const`。
- 句柄不能参与普通运算、比较或类型转换。
- 句柄不能作为内置事件语句或自定义事件块的普通实参。
- 句柄只能用于 `await`、`kill` 和 `status`。

### 15.3 `await`

语法：

```pevt
await a
var result : bool = await a
```

规则：

- `await` 后必须跟一个当前环境中已经定义并初始化的句柄名称。
- `await` 强制当前流程等待对应异步操作结束。
- 如果句柄对应的异步定义声明了返回值，`await` 是一个该返回类型的表达式，并直接取得句柄保存的返回值。
- 如果句柄对应的异步定义没有返回值，`await` 只能作为事件语句使用，不产生表达式值。
- 对已经成功完成的句柄执行 `await` 时，直接取得已经保存的结果，不重复执行异步操作。
- 如果句柄异常结束或已经被 `kill`，PolarisEvent 在执行该 `await` 时产生运行时异常，并且不向接收目标赋值。
- 静态分析不判断异步操作是否可能异常结束，也不因此拒绝加载事件。

### 15.4 `kill`

语法：

```pevt
kill a
```

规则：

- `kill` 后必须跟一个当前环境中已经定义并初始化的句柄名称。
- `kill` 强制停止句柄对应的异步操作。
- 被成功停止的异步操作视为异常结束，之后 `status` 返回 `2`。
- `kill` 不产生表达式值。

### 15.5 `status`

语法：

```pevt
var state : int = status a
```

规则：

- `status` 后必须跟一个当前环境中已经定义并初始化的句柄名称。
- `status` 不等待异步操作，仅立即读取当前状态。
- `status` 是结果类型固定为 `int` 的表达式。
- 返回 `0` 表示异步操作正在执行。
- 返回 `1` 表示异步操作已经成功完成。
- 返回 `2` 表示异步操作因执行错误或被 `kill` 而异常结束。

### 15.6 `await all` 与 `await any`

等待全部句柄：

```pevt
var completed : int = await all(a, b, c)(resultA, resultB, resultC)
```

等待任一句柄正常完成：

```pevt
var first : int = await any(a, b, c)(resultA, resultB, resultC)
```

放弃所有句柄的普通返回值：

```pevt
await all(a, b, c)()
await any(a, b, c)()
```

通用规则：

- `await` 的集合等待模式只能是 `all` 或 `any`，不能使用其他值。
- `all` 或 `any` 后的第一组括号是句柄列表，并且至少包含一个句柄。
- 句柄列表只能包含当前环境中已经定义并初始化的句柄名称。
- 多个句柄使用逗号 `,` 分隔。
- 同一个句柄不能在同一列表中重复出现。
- 句柄列表之后必须再写一组结果绑定括号。
- 结果绑定列表只能是空列表，或为每个句柄完整提供一个对应的变量名称。
- 不能只为部分句柄提供结果变量。
- 空结果绑定列表 `()` 表示放弃所有句柄保存的普通返回值。
- 非空结果绑定列表中的名称是新变量声明，不需要显式指定类型。
- 带非空结果绑定列表的集合等待只能作为一条独立事件语句，或者作为变量/常量初始化器或普通赋值右侧的完整表达式。
- 带非空结果绑定列表的集合等待不能嵌入运算、括号、调用参数或其他更大的表达式中。
- 每个结果变量的类型从对应句柄所代表异步定义的普通返回值类型中自动推断。
- 结果变量在整条集合等待语句完成后进入当前环境并可以被后续语句引用。
- 使用非空结果绑定列表时，每个句柄对应的异步定义都必须声明普通返回值类型。
- 结果变量属于当前变量环境，其名称不能与已有变量、常量、参数、句柄或同一绑定列表中的其他名称重复。
- 正常完成的句柄会使用其保存的返回值初始化对应结果变量。
- 异常结束或被 `kill` 的句柄不会初始化对应结果变量。
- 静态分析不对上述异步结果变量执行确定赋值检查；运行时读取未被初始化的结果变量时，由 PolarisEvent 故意产生运行时异常。
- 在集合等待中，正常结束和异常结束都视为该句柄已经结束。
- `await all` 和 `await any` 的表达式结果类型均固定为 `int`。
- 二者均可作为事件语句使用；此时放弃其 `int` 结果。

`await all` 规则：

- `await all` 等待列表中的所有异步操作全部结束。
- 任何操作无论正常结束还是异常结束，都会计入“已经结束”。
- 返回值是正常结束的异步句柄数量。
- `await all` 不主动停止任何句柄。

`await any` 规则：

- `await any` 等待列表中的任意一个异步操作正常结束。
- 某个句柄异常结束时，只将其标记为已经结束，并继续等待其他尚未结束的句柄。
- 第一个正常结束的异步操作出现后，立即停止等待，并对列表中其他尚未结束的异步操作执行 `kill`。
- 返回值是第一个正常结束句柄在输入列表中的序号，序号从 `1` 开始。
- 如果多个句柄在同一时刻正常结束，返回输入列表中序号最小的句柄序号。
- 如果列表中的句柄最终全部异常结束，则返回 `0`，此时不存在正常结束的句柄。

### 15.7 PolarisEvent 调度与生命周期

- PEVT 的全部异步操作由 PolarisEvent 运行时创建、调度、等待和终止。
- 异步语法不翻译为原始游戏 DSL 的并行事件；解释器必须为其创建或维护独立运行状态。
- 每个异步操作归属于启动它的当前 PolarisEvent 执行实例。
- 自定义事件块内启动的异步操作仍归属于当前外层 PolarisEvent 执行实例，不随事件块 `return` 自动转移所有权。
- 未保存句柄的异步调用同样由当前 PolarisEvent 执行实例持有和跟踪。
- 异步 `callevt` 创建的子事件及其进一步启动的异步操作必须参与级联所有权和清理；停止父事件时不能遗留仍在运行的子事件树。
- 当前事件执行到任意可达 `end` 时，PolarisEvent 必须对其拥有且尚未结束的全部异步操作自动执行 `kill`。
- 当前事件因停止、替换、异常或运行时卸载而提前终止时，执行相同的自动清理。
- 已经正常或异常结束的异步操作不重复终止。
- 自动清理发生后，不允许该事件启动的特效、UI、回调或全局状态操作继续影响后续事件。
- 异步操作的 PolarisEvent 协程、自定义等待类型、取消和句柄的内部实现契约见 `PEVT-异步协程与等待模型.md`。

## 16. 控制流

- PEVT 加载阶段必须静态分析 `if`、`elif`、`else`、`endif`、`while`、`endwhile`、`switch`、`case`、`default`、`endswitch`、标签和 `goto` 形成的控制流。
- 每一条可达的事件退出路径都必须以 `end` 终止。
- 事件路径不能直接到达文件末尾。
- `goto` 转移控制后，不继续执行 `goto` 后面的顺序路径。
- `end` 终止当前路径，不执行该路径中位于 `end` 后面的语句。
- 无法到达的事件语句产生静态警告，不阻止事件进入可执行状态。
- 静态分析不尝试证明 `while`、向后 `goto` 或其他控制流是否会无限执行。
- 控制流可能重复经过变量或常量声明时，静态分析不因此拒绝加载事件。
- 同一次事件或自定义事件块调用的运行环境中，如果控制流再次执行一个已经执行过的同名变量或常量声明，PolarisEvent 必须产生运行时异常。
- 普通变量在声明完成后的 `=` 重新赋值不属于重复初始化，仍按变量赋值规则执行。
- 无限循环、无进展循环及重复回跳由 PolarisEvent 在运行时监控和处理。

### 16.1 运行时诊断

PEVT 的运行时异常、警告、传播与清理规则记录在独立文件：

```text
PEVT-运行诊断表.md
```

## 17. 最小合法文件

```pevt
id "MuseumEntrance"

end
```

## 18. 非法文件

缺少事件 ID：

```pevt
end
```

缺少 `end`：

```pevt
id "MuseumEntrance"
```

重复声明事件 ID：

```pevt
id "MuseumEntrance"
id "AnotherEvent"

end
```

未定义标签：

```pevt
id "MuseumEntrance"

goto #Missing
end
```

独立出现的 `elif`：

```pevt
id "MuseumEntrance"

elif 条件
    end
```

缺少 `endif`：

```pevt
id "MuseumEntrance"

if 条件
    end
```

缺少 `endwhile`：

```pevt
id "MuseumEntrance"

while 条件
    end
```

空的 `switch`：

```pevt
id "MuseumEntrance"

switch 条件
endswitch
end
```

重复的 `case` 表达式：

```pevt
id "MuseumEntrance"

switch 选择值
case 1
case 1
endswitch
end
```

## 19. 当前语法

```ebnf
document          = id-declaration, { capability-enable-declaration },
                    { custom-block-definition | event-statement },
                    end-of-file ;
id-declaration    = "id", event-id-literal ;
capability-enable-declaration = "enable", ( "cs" | "async" ) ;

event-statement   = end-statement
                  | if-statement
                  | while-statement
                  | switch-statement
                  | label-statement
                  | goto-statement
                  | variable-declaration
                  | constant-declaration
                  | assignment-statement
                  | handler-declaration
                  | event-call-statement
                  | builtin-statement
                  | custom-block-call-statement
                  | block-return
                  | await-statement
                  | kill-statement
                  | exec-statement
                  | raw-statement ;

end-statement     = "end" ;

if-statement      = "if", expression, { event-statement },
                    { "elif", expression, { event-statement } },
                    [ "else", { event-statement } ],
                    "endif" ;

while-statement   = "while", expression,
                    { event-statement },
                    "endwhile" ;

switch-statement  = "switch", expression,
                    switch-arm,
                    { switch-arm },
                    "endswitch" ;

switch-arm        = case-arm | default-arm ;
case-arm          = "case", case-expression, { event-statement } ;
default-arm       = "default", { event-statement } ;

label-statement   = "#", identifier ;
goto-statement    = "goto", ( "#", identifier | case-expression ) ;

variable-declaration = "var", identifier, ":", type-name,
                       [ "=", expression ] ;

constant-declaration = "const", identifier, ":", type-name,
                       "=", expression ;
assignment-statement = variable-reference, "=", expression ;

builtin-statement = builtin-call ;
builtin-call      = "@", callable-name, "(",
                    [ expression, { ",", expression } ],
                    ")" ;
builtin-signature = [ "async" ], "@", callable-name, "(",
                    [ parameter-declaration,
                      { ",", parameter-declaration } ],
                    ")", [ ":", type-name ] ;

event-call-statement = event-call ;
event-call           = "callevt", event-id-literal ;

custom-block-definition = [ "async" ], "block", custom-block-signature,
                          { event-statement },
                          "endblock" ;
custom-block-signature  = "_", callable-name, "(",
                          [ parameter-declaration,
                            { ",", parameter-declaration } ],
                          ")", [ ":", type-name ] ;
parameter-declaration   = identifier, ":", type-name ;
block-return            = "return", [ identifier ] ;

custom-block-call-statement = custom-block-call ;
custom-block-call       = "_", callable-name, "(",
                          [ expression, { ",", expression } ],
                          ")" ;

handler-declaration = "handler", identifier, "=", handler-initializer ;
handler-initializer = builtin-call | custom-block-call | event-call ;
await-statement     = await-operation ;
kill-statement      = "kill", identifier ;
exec-statement      = "exec", "(", expression, ")" ;

raw-statement     = raw-cmd-statement | raw-cs-statement ;
raw-cmd-statement = "$raw", "cmd", raw-block ;
raw-cs-statement  = "$raw", "cs", [ raw-cs-arguments ], raw-block ;
raw-cs-arguments  = "(", identifier, { ",", identifier }, ")" ;
raw-block         = "'''", raw-content, "'''" ;

type-name         = "int" | "float" | "bool" | "char" | "string" ;

expression        = operand, { binary-operator, operand } ;
operand           = unary-expression
                  | grouped-expression
                  | primary-expression ;
primary-expression = variable-reference
                   | literal-expression
                   | conversion-expression
                   | builtin-call
                   | custom-block-call
                   | raw-cs-expression
                   | await-expression
                   | status-expression ;

case-expression   = case-operand, { binary-operator, case-operand } ;
case-operand      = case-unary-expression
                  | case-grouped-expression
                  | case-primary-expression ;
case-primary-expression = variable-reference
                        | literal-expression
                        | conversion-expression ;
case-unary-expression   = ( "!" | "-" ), case-operand ;
case-grouped-expression = "(", case-expression, ")" ;

variable-reference     = identifier ;
literal-expression      = literal ;
literal                 = integer-literal
                        | float-literal
                        | string-value-literal
                        | char-literal
                        | boolean-literal ;
integer-literal         = digit, { digit } ;
float-literal           = digit, { digit }, ".",
                           digit, { digit } ;
string-value-literal    = string-literal, { string-continuation } ;
string-continuation     = "+", physical-line-break,
                          aligned-string-literal ;
aligned-string-literal  = string-literal ;
string-literal          = '"', { string-character | string-escape }, '"' ;
char-literal            = "'", ( char-character | char-escape ), "'" ;
boolean-literal         = "true" | "false" ;
event-id-literal        = '"', event-id-character,
                          { event-id-character }, '"' ;
event-id-character      = ascii-letter | digit | chinese-character ;
identifier              = ( ascii-letter | "_" ),
                          { ascii-letter | digit | "_" } ;
callable-name           = ascii-letter,
                          { ascii-letter | digit | "_" } ;
ascii-letter            = ? ASCII A-Z or a-z ? ;
digit                   = "0" | "1" | "2" | "3" | "4"
                        | "5" | "6" | "7" | "8" | "9" ;
chinese-character       = ? Unicode Unified_Ideograph code point ? ;
conversion-expression   = "(float)", variable-reference
                        | "(string)", variable-reference ;
raw-cs-expression       = "$raw", "cs", [ raw-cs-arguments ], raw-block ;
await-expression        = await-operation ;
await-operation         = "await", ( identifier | aggregate-await ) ;
aggregate-await         = ( "all" | "any" ),
                          "(", identifier, { ",", identifier }, ")",
                          "(", [ identifier, { ",", identifier } ], ")" ;
status-expression       = "status", identifier ;
grouped-expression      = "(", expression, ")" ;
unary-expression        = ( "!" | "-" ), operand ;

binary-operator   = "+" | "-" | "*" | "/" | "%"
                  | "<" | "<=" | ">=" | ">" | "==" | "!="
                  | "&" | "|" | "^" ;

exec-fragment     = { event-statement }, end-of-file ;
physical-line-break = ? source LF or CRLF normalized as one line break ? ;
```

语义约束：

```text
document 的全部可达事件退出路径必须以 end-statement 终止。
aligned-string-literal 的开始双引号必须与所属 string-value-literal
第一段 string-literal 的开始双引号位于同一源文件列。
exec-fragment 递归禁止 id、enable、block、endblock、return、end、标签和 goto；
exec-fragment 到达自身 end-of-file 时正常返回调用位置。
```
