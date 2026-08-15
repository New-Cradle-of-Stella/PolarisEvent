# PEVT 原版 CMD 别名审计

## 数据范围

- 游戏版本目录：`AliceInCradle_ver029`。
- `evt/__vp_person.dat`：18 个稳定说话人键，其中 `_` 是默认叙述者；其余 17 个视觉键归并为 15 个故事人物。
- `evt/__vp_talker_pos.dat`：58 个固定站位键。
- 726 个 `.cmd`，共 312 处 `TALKER_REPLACE`。
- `EvImg` 中已核对 17 组人物 PXLS 数据 Bundle 与对应 texture Bundle 均存在；`_` 不声明 PXLS。

## 稳定人物键

| 原键 | 原名 token | 对话音效 | 色标 | 图标 | PXLS | 生命周期 | PEVT 映射 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `_` | `＊` | — | — | — | — | — | `aic:narrator` |
| `n` | `Noel` | `talk_noel` | `#DCCAE7` | `IconNoel0` | `__ev_n` | static | `aic:noel/default` |
| `nb` | `Noel` | `talk_noel` | `#DCCAE7` | `IconNoel0` | `__ev_n_bass` | event | `aic:noel/bass` |
| `nb2` | `Noel` | `talk_noel` | `#DCCAE7` | `IconNoel0` | `__ev_n_epbench` | event | `aic:noel/epbench` |
| `v` | `Laevi` | `talk_levi` | `#C692F7` | `IconLaevi` | `__ev_v` | event | `aic:laevi/default` |
| `p` | `Primula` | `talk_m1` | `#E7AD96` | `IconPrimula` | `__ev_p` | event | `aic:primula/default` |
| `i` | `Ixia` | `talk_ixia` | `#B7FABF` | `IconIxia` | `__ev_i` | event | `aic:ixia/default` |
| `t` | `Nightingale` | `talk_nightingale` | `#A5AFE0` | `IconNightingale` | `__ev_t` | event | `aic:nightingale/default` |
| `d` | `Tilde` | `talk_tilde` | `#DAD1CA` | `IconTilde` | `__ev_d` | event | `aic:tilde/default` |
| `l` | `Alma` | `talk_alma` | `#FFC9DE` | `IconAlma` | `__ev_l` | event | `aic:alma/default` |
| `f` | `NoelDad` | `talk_nodad` | `#C4B8BC` | `IconDelfini` | `__ev_f` | event | `aic:noel-father/default` |
| `g` | `Mepha` | `talk_mob_w3` | `#9EA3B1` | `IconMepha` | `__ev_g` | event | `aic:mepha/default` |
| `s` | `Ostrea` | `talk_mob_m2` | `#FFD9C0` | `IconOstrea` | `__ev_s` | event | `aic:ostrea/default` |
| `w` | `Walross` | `talk_mob_m1` | `#B1A8BE` | `IconWalross` | `__ev_w` | event | `aic:walross/default` |
| `bt` | `Barten` | `talk_barten` | `#FF8686` | `IconBarten` | `__ev_bt` | event | `aic:barten/default` |
| `so` | `Tigrina` | `talk_soala` | `#E1FC8A` | `IconTigrina` | `__ev_so` | event | `aic:tigrina/default` |
| `a` | `Alice` | `talk_alice` | `#B7C3FC` | `IconAlice` | `__ev_a` | event | `aic:alice/default` |
| `fh` | `FirstHuman` | `talk_barten` | `#C67F8A` | `IconFirst` | `__ev_fh` | event | `aic:first-human/default` |

`event` 表示原表 `%PXL_PERSON ... 1` 会在事件资源清理时释放外置图；`static` 表示原版将其作为常驻 loader。PEVT 的 PolarisRes 桥只借用原版所有权，不自行复制这个生命周期。

## 动态 `TALKER_REPLACE` 键

以下键至少被一条 CMD 动态创建或改写：

```text
a, ann, b, bs, bs0, bs1, bs2, bt, cane, cm, cn, dev, dj, djt,
fd, ff, fh, fm, g, i, ixiacane, l, ma, mb, mb0, mb1, mb2, mb3,
mb4, mb5, mb6, mc, mob, n, noelcane, ow, p, pp, s, so, st, t,
tc, v, w, x, xa, xb
```

其中 `a/bt/fh/g/i/l/n/p/s/so/t/v/w` 也属于稳定人物键，但原版 CMD 会临时改写其姓名或音效。同一个纯动态键并不稳定，例如：

| 键 | 已观察含义 |
| --- | --- |
| `b` | `Customer`、`elf_man` |
| `bs` | `instructor`、`teacher` |
| `mb` | Mob、Army、engineer、laboman 等多种临时说话人 |
| `x` | Customer、Army、Guard、elf_madam、elf_student、Mob 等 |
| `a` | 稳定 Alice 键，也会在洞窟事件中临时替换为 Mob |

结论：这些动态键只能留在 `$raw cmd` 会话内，不能生成 PEVT 固定人物 ID。

## 原版站位键

原表共有 58 个键：

```text
L R C CL CR LL RR LLH RRH CCL CCR CLL CRR
CCLCT CCLCB CCRCT CCRCB CLBB_ZCOS
T TT B BB CLT CRT CLB CRB RT RTT LT RB LB RRT LLT LTT RRB LLB
CCT CCB CT CB CCLB CCRB CCRBB CCLT CCLTT CCRT CCRTT CRRTT CLLTT
BBR BBL ROUT RBOUT RTOUT BOUT LOUT LBOUT LTOUT
```

其中大量键直接编码了相对坐标、入场起点、上下偏移和 easing。PEVT 只固定常用语义锚点；其余通过 `.pactor` 的数值 Anchor 扩展，不把这些组合缩写重新包装成另一批必须背诵的名字。

## 资源核对

- `EvImg/<pxl>.pxls.dat` 和 `EvImg/<pxl>.pxls.bytes.texture_0.dat` 对上述 17 个视觉键均存在；默认叙述者 `_` 无视觉 Bundle。
- 已观察的原版地图角色 PXLS 包括 `PxlNoel/noel.pxls` 与 `MapChars/sub_a/sub_i/sub_l/sub_p/sub_s/sub_so/sub_t/sub_v/sub_w` 等；地图视觉不等于事件人物身份，只有内置目录明确登记后才绑定。
- 原版 `EvPxlsLoader` 使用 `MTI("EvImg/<name>.pxls")` 加载数据，用 `MTIOneImage` 加载 texture Bundle，再调用 `ReplaceExternalPng` 与 `MTRX.assignMI`。
- `EV.addExternalPxlsAfter` 只适合原版 EV 人物表扩展，并不能直接替代新人物目录；普通 PEVT 仍使用自己的注册表和显示服务。
