# PROJECT_STATE —— 铁幕:前线 Demo 项目交接文件

> 用途:新会话续工时,先读本文件即可恢复全部上下文。每次里程碑更新本文件。
> 最后更新:2026-09-06 | 阶段:M6.1 收口(198/198)+建筑视野扩大一倍(200/200;塔24/大本营40/其他20);下一步=M6.2 战斗 UI(顶栏/飘字/血条/选中/射程)→ M6.4 特效音效/计时加时积分结算;**待用户编辑器 Play 审:M6.1+M8.6+M8.7 全部 View 视觉(小地图/雾观感/框选/撤退按钮)**

> ## ★★ 新窗口启动协议(2026-09-06 用户拍板,常驻)
> **用户会直接开新窗口来控制 GLM 额度成本(长会话上下文膨胀 → 编排循环 token 消耗快),不需要向你解释前因后果;进度不允许因此中断。**
> 新会话按此清单冷启动(勿再追问用户背景):
> 1. 通读本文件(统一规格+进度+红线+坑全在此);
> 2. `git log --oneline -8` 对齐最近落地;
> 3. 从「进度」节第一个未完成项继续,按既有协议(门禁→GLM→测试→全绿即 commit→归档本文件);
> 4. 只读下一个模块**需要的**文件(grep 定位优先),不要整库通读,防上下文再次膨胀;
> 5. 每个模块收口后主动提示用户"可以开新窗",由用户决定。
> 红线速查:GLM 消耗必须有收益/生图必人工复核后才生3D/编排者非必要不下场/全绿即 commit 未测绿禁止 commit/Sim 禁 UnityEngine/**多模态预检必做且风险点必主动上报原因**(2026-09-06 新增)。
>
> ## ★ 2026-09-05 新 Agent 流程(换窗后生效,用户拍板)
> - **deepseek-v4-pro(结构化转述)**:阅读用户报告/拍板内容,结构化输出给主编排;**输出必须严格贴合原文意思**,不增删改动。
> - **GPT(主编排,用户拟接入 GPT-6 Astra / GPT-5.6 Sol)**:拆解需求、规划整体、分发任务、检查验收。
> - **美术与代码模型不变**:程序主程=GLM-5.3(glm_call.py);视觉验收=hy-vision-2.0-instruct(TokenHub);生图/3D/动画=TokenHub 管线(team-pipeline);审美终审=用户。

## 1. 任务与目标
把《铁幕:前线》GDD v1.6 做成**单线闭环可玩 Demo**,完成度=产品级与 demo 之间(打磨后可发布)。
计划文件:`D:\DevTools\IronCurtain\docs\demo-implementation-plan.md`(v1.1,含 7 阶段 24 模块、10 条拍板项 D1–D10 已按推荐值批准、设计变更记录)。
GDD 原文:`D:\DevTools\IronCurtain\docs\GDD原文-铁幕前线.txt`。

### 已批准的设计变更(优先于 GDD 原文)
1. **星落移除「攻击兵营降产 50%」**,保留沉默禁冲、打磐石护盾减半。
2. D1–D10 按计划文件推荐值执行(攻城锥 15 直伤 / 阵地 30 血堡垒驻 3 人 30 秒 / 箭塔驻军每人+2 攻击 / 海克斯不做胜率动态 / AI 简单+普通 / 加时积分照 GDD / 程序化音效无 BGM / 单线含 1 中立资源点 / 3 步新手浮层 / 简易结算)。
3. **地图规格迭代史(三次,已收纳为终版,见下「当前统一规格」)**:
   - v1 误实现:整线 3×30(GDD 2.2 本意是"相邻建筑之间每段线空间",实现误读);
   - v2(2026-09-04):6 排×255 列长条 + 连续坐标自由移动(红警式:直线+建筑碰撞圆+单位软碰撞,1 格=1 米,欧氏索敌);
   - **v3(2026-09-05,当前)**:椭圆形大地图 + 楚河汉界河(宽 10,上下各 5 格桥,涉水×0.5)+ 河中央岛。详见 `docs/大地图改造拆解.md` 与 `docs/连续坐标改造.md`。
4. **美术路线(迭代史→当前)**:程序化自建网格(否决)→ Kenney CC0 卡通低模(过渡资产,已导入 `Assets/Resources/Models`,当前画面用它)→ **终版方向:TokenHub AI 管线(生图 Seedream → 3D 混元 hy-3d-3.0 → 动画 hy-3d-motion),风格待定稿**(部落冲突风已弃,那只是降低实现难度的临时选择;地形风格方向=英雄联盟式石路+少量草地)。六英雄正式模型尚未生成。
5. **交换规则(2026-09-05 编排修复,已入测试)**:交换只消耗正式驻军,**正式守军被肃清即城池易主**(临时驻军溃逃);敌方攻城意图存续期间临时驻军不转正式。D18 城门=建筑朝敌侧碰撞半径+0.6 米起向外延展(椭圆内、非水域)。
6. **2026-09-05 用户拍板·操作 UI**:兵营**总览面板**(通用多兵营,每兵营一区块:3 队列+头像产兵+取消;边界态=被摧毁/被占领)。
7. **2026-09-05 用户拍板·美术红线**:生图结果在**生 3D 前必须人工复核**——把图片在对话框展示或告知存储路径,制作人明确确认后才允许图生 3D。未确认生 3D = 流程事故。已写入 `team-pipeline/README.md` 顶部红线节。
8. **2026-09-05 用户拍板·商业化底线**:公平竞技,**永远不做局外养成;卖东西不卖数值**(皮肤=唯一付费点,纯换皮,永不动数值)。新英雄按"职业槽/对策卡"出(用户例:法师=大范围 AOE、慢射速、高伤害、破防,作为对策卡)。
9. **2026-09-05 用户方向·将兵制(用户已定稿大部分,剩余 3 个微点待确认)**:
   - 兵团=1 将+20 兵,兵随将走、红警式可调遣;**将上限:正式 8、测试版 3**;将战死=退役,士兵**永不溃逃、就地就近逐杀至死**(无将状态继续战斗;取消并团/溃散逻辑,简化)。
   - **生产分工:兵营只承担补兵;将一律从主基地产出,出场自带满编 20 兵兵团;将产出 20s/将**(参考守望复活,含赶路成本;60s 过重被否)。测试期不做选将/BP,**六将均可直接产**;最终 demo 选 3 将,选将/BP 界面另立模块。
   - **补兵(全局池,平拍)**:全局 2s/兵、池上限 60(正式/测试同值);兵营只承担补兵生产;玩家点补兵按钮→全局 SJF(最缺人兵团先补)→士兵入队前完成变装;**补兵行军表现直接做:士兵从各生产点(主基/兵营)走出,行至兵团入队**。交战兵团不可补,脱战 3s 入列。
   - **将**:正式上限 8、**测试版开局 3 将、上限 4 将**;主基地产将、20s/将、出场自带满编 20 兵;测试期六将均可直接产,选将/BP 另立模块。
   - **溃逃/封城(用户 2026-09-05 明确)**:封城下临时驻军不参与交换、不进正式驻军、**城后等候**;城易主→自动溃逃(不变);城守住→恢复临时驻军并补入正式驻军。**等候部队保持玩家可调遣,可主动调去截击敌方增援(大地图手动操作,无需新机制,调遣即截击)**。
   - **建筑占格(推翻早期"逻辑单格"决策)**:改为按视觉占地**多格占用**(该站多少格占多少格);尺寸表待定,建议配置化(随新美术定稿调整)。
   - 攻城交换只消耗兵,将留守城外;士兵属性=将 1/10(盾/血/攻)、攻速移速同将;将战死士兵不溃逃、就近逐杀至死。
   - **士兵视野:近战 3、远程 6**(用户 2026-09-05 拍板;近远程按将的武器类型:坠日/星落=远程,其余近战)。
   - **新待定数值(拆解前需拍)** → 用户"开工"默认全部采纳:①正式驻军 30 兵/城、临时 30、交换 1:1 兵/次、节奏沿用 Siege 参数;②占地初值:基地 5×5/箭塔 4×4/兵营 5×4/资源点 4×4/堡垒 3×3(配置化,中心锚定,奇数格为佳);③行军补兵 demo 不可被截击(纯表现);④测试版开局 3 将=磐石/断峰/坠日。拆解文档:`docs/将兵化改造拆解.md`。

### 当前统一规格(权威,迭代历史已并入;新会话/新主编排一律以此为准)
- **地图**:椭圆,包围盒 Row 0..29 × Col 0..285;中心 (143,15),长半轴 143、短半轴 15;单位不可越界(TerrainMap.ClampToEllipse)。
- **河**:X∈[138,148)(宽 10);上方桥 X∈[138,148)&Y∈[25,30),下方桥 X∈[138,148)&Y∈[0,5);河中央岛 X∈[139,147)&Y∈[11,19);涉水速度×0.5,桥/陆/岛正常。
- **建筑布局**(全部 Row=15,间距 30):蓝 Base 15 / 蓝兵营 46 / 蓝资源点 77 / 蓝箭塔 108 / 中立资源点 143(岛)/ 红箭塔 178 / 红资源点 209 / 红兵营 240 / 红 Base 271。集结点:蓝 (15,270)、红 (15,16)。
- **移动**:连续坐标 `Entity.PosF`(SimVec2,X=列米,Y=行米);直线+建筑碰撞圆(半径 Base3.2/Barracks2.4/Tower2.2/RP1.6/Fort1.6)+软碰撞(0.9m)+椭圆边界+涉水减速;`Entity.Pos` 格与 PosF 同步(ToGrid);单位不占格。
- **距离**:全部欧氏米制;近战攻击判定 Range+0.3;拆塔近战距离=碰撞半径+0.4;驻军出击阈值 2.5m。
- **规则继承**:GDD v1.6 全数值 + D1-D35 拍板项 + 交换规则(变更#5);攻城逻辑(意图/封城/交换/拆塔)与伤害公式(D28)无改动。
- **测试**:135/135 全绿(EditMode batchmode -runTests);测试布局坐标须在椭圆内(统一平移规则:旧 (row 0-2, col 0-29) → (row+14, col+108))。
- **渲染**:URP 17.0.3 + Bloom 后处理;当前 View 用 Kenney 过渡资产(城堡塔/角色/风车),世界坐标 x=X 米、z=3−Y;待 M6.6 换椭圆石路+草地/河/桥渲染与新高相机(≈1/3 地图视野)。

10. **2026-09-06 用户拍板·GLM 用量原则**:GLM-5.3 可以当驴用、大量消耗 tokens(一次 10% 级别可接受),**但大量消耗必须有效益**(效率更高/质量更好等均可作为理由);**空耗 tokens 禁止**。
11. **2026-09-06 用户拍板·职责红线**:deepseek-v4-pro=**总编排**,以"**若非必要不亲自下场**"为准则(代码/测试优先派 GLM,编排者只做拆解/门禁/验收/根因诊断)。
12. **2026-09-06 用户新需求·自动布阵(待拆解,明天安排)**:玩家**框选多兵团**后出现**渐渐淡去直到消失的字符**(提示性文字),按 **T 键**自动布阵。布阵规则:**重装在前、战士居中、射手在后**(重装=磐石/镇岳,战士=断峰/逐风,射手=坠日/星落);战场局势复杂(**多向受敌**)时自动让重装与战士**保护射手**(射手居中)。框架沿用原自动站位逻辑的框架。依赖项:框选指挥(原 M6.3 点选框选)尚未实现,需一并排期。**待确认:①布阵单元=兵团原子(将排阵、兵随将)还是士兵级重排;②阵型朝向(移动目的地/最近敌/玩家指定);③"多向受敌"判定口径(扇区数量/角度)。**12b. **2026-09-06 自动布阵规格(用户答疑,权威)**:
   - **布阵单元=兵团原子**:将排阵、兵随将走,士兵不打散重编。
   - **阵型朝向**:①视野内有敌人且**该视野来自同一框选内的友军** → 自动朝敌军方向布阵;②视野内无敌人 → 面朝右侧(玩家大本营在左侧;敌方玩家面朝左侧),或以前进方向为准(前提是朝右=朝敌方大本营方向);**若往左移动(后撤),除非有敌人,否则保持面朝敌人方向(不转身)**。
   - **射手行为**:有敌人时,**射手主动向近战侧位移**(向己方近战侧靠拢),**直到近战交战才开始攻击**(交战前只机动不开火——待与用户确认语义,见下)。
   - **多向受敌**:敌人分布扇区 >120° 时,大致**以射手兵团为圆心**,近战兵团自动列成**扇形外侧"屏风"**保护射手。
   - 待确认语义:①"射手向近战侧位移"=向己方近战单位侧靠拢(非向敌),对吗;②"直到近战交战,射手才攻击"=射手交战时保持不开火、待近战接战后才射击(hold-fire),对吗。
   - **射手开火条件(用户 2026-09-06 修正,权威)**:己方近战已交战,**或**己方近战位于敌人与射手之间且近战与射手距离 ≥3 → 射手开火;否则射手只机动靠向己方近战侧。
13b. **2026-09-06 兵团自主行为层(用户新规,权威)**:
   - **警戒**:己方兵团约 **20 格**内视野出现敌军 → 进入警戒状态(布阵朝向/备战)。
   - **支援**:警戒中的己方兵团若发现**己方友军兵团交战** → 自动前往支援;**玩家操作优先,可随时打断**。
   - **主动进攻**:兵团 **15 格**内出现敌人 → 自动寻路发动进攻;**优先级高于支援**。
   - **边界(防拉回)**:玩家操作接管后,该次警戒/支援的重新判定**不得再把兵团拉回去**(已触发的自动决策对玩家命令让位且不复读)。
   - 待确认:①玩家打断后,同一警戒事件永久失效?新事件(如新敌 15 格内)是否仍可触发自动行为;②"20 格/15 格"以兵团主将位置为圆心计,对吗;③支援目标选择:最近交战友军?敌我兵力比优先?

13. **2026-09-06 用户预告**:将兵新逻辑下,**兵种射程体系可能重构**(明天可能拍板,编排时注意与自动布阵、士兵视野 3/6 联动)。
14. **2026-09-06 用户拍板·建筑视野扩大一倍**:塔 12→**24**、大本营 20→**40**、其余建筑 10→**20**(VisionSystem.BuildingVisionRadius;M5.0 原值作废,规格文档表已同步;AI 只读 VisionSystem 视图,视野扩大后 AI 决策信息更多=拍板自然结果)。**流程规矩(同拍板)**:①多模态预检入验收流程——每次截图验收必须逐张问"画面基本逻辑+主观风险点(糊/过曝/构图)",发现风险点必须连同原因主动上报制作人,禁止只报客观项通过;②人工复核红线仅限 TokenHub 生图→生3D 环节,程序化过渡资产(如 M6.6 Kenney 地形)不占用制作人终审时间;③M6.6 全图"糊/眼疼"根因已定位:相机 55m 高+orthoSize 27(视野宽 ~48m)对 2.4m 单位 → 1080p 下单位仅 ~55px,细节不可辨;叠加 Bloom(threshold 0.55/intensity 1.7)使灰石高频棋盘纹理高光泛白。终版 M6.6 已规划的新相机(≈1/3 地图视野,单位放大 ~3 倍)+TokenHub 美术管线一并解决,程序化地形不再单独打磨。
13c. **2026-09-06 射程/行为修订(权威)**:
   - **射程(非行军状态)**:近战(磐石/镇岳/逐风)=2 格,断峰=3 格,射手(坠日/星落)=9 格。士兵射程继承主将。
   - **玩家控制豁免**:被玩家直接控制的军队**可不执行支援类指令**;**但视野内出现敌人仍被迫接战**(不主动支援≠不接战)。
   - **主动攻击(修订)**:兵团**视野范围内出现敌人**即自动寻路进攻(原"15 格"作废);优先级高于支援。
   - **警戒/支援(修订,原 20 格警戒作废)**:友军兵团(含其小兵视野)发现敌人→按主动攻击自动接战;若该友军主将距己方主将 **≤15 格** → 己方**无需指令自动支援**。
   - **撤退(新)**:交战中兵团被玩家主动授意撤退(点主将或框选出现撤退 UI,点 UI 执行);**撤退期间血量与攻击力 −25%**;**所有撤退/溃逃同理**;撤退目标=最近己方据点,在附近站定。
   - 待确认(M8.7 已按默认口径落地,待用户复核):①撤退 −25% 已按"乘区近似"落地(输出−25%/承伤+25%,撤退结束自动消失,与溃逃同构,溃逃系数同步 0.3→0.25);②玩家控制兵团**维持现状=视野内敌人仍自动寻路进攻**(13c 字面只豁免支援类;若要"只被迫接战不主动寻路"需再拍板);③断峰归"战士(近战)"档已是现状(M8.5c HeroClass 三分类),无需改。

## 2. 环境事实(本机)
- OS:Win11 专业版;32GB 内存;RTX 5060 Ti(驱动 32.0.16.1656,与 Unity Hub Chromium 硬件加速冲突→Hub 需 --disable-gpu 启动,桌面快捷方式已建)
- Unity 6000.0.30f1 LTS 装在 `D:\Unity\Editor`(注意:是 2024-12 的 LTS 补丁,不是最新;国内 CDN 只镜像到这个版本)
- **项目:`D:\DevTools\IronCurtain`**(Unity -createProject 生成,完整标准骨架);旧实验工程 `D:\DevTools\IronCurtainDemo` 是备份
- Python:`C:\Users\HP\AppData\Local\Programs\Python\Python312\python.exe`;有 venv 在 `D:\DevTools\unity-mcp-main\Server\.venv`(含 fastmcp/pillow/pypdf,优先用它跑脚本)

## 3. 模型管线(核心能力)
| 角色 | 模型 | 调用方式 |
|---|---|---|
| 代码主力 | GLM-5.3 | **Anthropic 兼容端点**:`https://open.bigmodel.cn/api/anthropic/v1/messages`,header `x-api-key: <key>`,`anthropic-version: 2023-06-01`,必须 `"thinking":{"type":"disabled"}` |
| 视觉验收 | **hy-vision-2.0-instruct**(TokenHub) | OpenAI 兼容 /v1/chat/completions + image_url(base64);flash 已弃用 |
| 结构化转述→逻辑挑刺 | deepseek-v4-pro | **挑刺岗常驻(2026-09-06 拍板)**:ZCode provider 直连 `deepseek_review.py`+`review_check.py` 门禁;窄口径=逻辑/时序/状态类;**v4-pro 须 thinking disabled**(默认开思考吃光 max_tokens);兼结构化转述(无视觉) |
| 总编排候选 | Kimi K3(待用户接入) | 2.8T MoE/1M ctx/Agent 强(Tau2);API ¥20 输入(缓存命中 ¥2)/¥100 输出 per 1M;**Kimi Coding Plan ¥49-699 五档,K3 需 Moderato(¥99)起**;社区已有"K3 主编排+GLM 主程序"同构实践;接入路径=Anthropic 兼容端点进 ZCode(同 bigmodel 模式) |
| 主编排(换窗后) | GPT(6 Astra / 5.6 Sol,待用户接入) | 拆解/规划/分发/验收 |
| 生图/3D/动画 | TokenHub 管线(team-pipeline) | seedream 生图 / hy-3d-3.0 3D / hy-3d-motion 动画 |
- **API key 位置**:`C:\Users\HP\.zcode\v2\config.json` → provider["builtin:bigmodel-coding-plan"].options.apiKey(明文,勿外传/勿回显)。读取代码见 `D:\DevTools\vision-mcp\glm_vision.py::get_api_key()`
- ⚠️ OpenAI 格式通道(open.bigmodel.cn/api/paas/v4)**无余额**(429),禁用
- 代码调用脚本:`D:\DevTools\glm-pipeline\glm_call.py`

## 4. 基础设施
- **Unity MCP**:服务端 `D:\DevTools\unity-mcp-main\Server\.venv\Scripts\mcp-for-unity.exe`,HTTP 模式常驻 `http://127.0.0.1:8080`(启动脚本 `D:\DevTools\start-mcp-server.cmd`);插件 com.coplaydev.unity-mcp v10.2 已挂进项目 manifest(file: 相对 Packages 目录);编辑器内 **Ctrl+Alt+M 开窗口点 Start** 才连接(每次新项目/重开编辑器都要点)
- **视觉验收 CLI**:`glm_vision.py <图> [问]`(旧,flash,备用);首选 hy-vision-2.0-instruct 走 TokenHub(见 team-pipeline README)
- 截屏:`powershell Add-Type System.Windows.Forms...CopyFromScreen` 存 PNG(见历史会话,或写脚本)
- 手册:`D:\DevTools\设置手册_Unity_MCP与引擎安装.md`(引擎/MCP/视觉系统全部文档)

## 5. 工作流协议(每模块)
- **美术管线(TokenHub,2026-09-05 已建)**:`D:/DevTools/team-pipeline/`(config.json 存 key + tokenhub.py 核心库 + gen_image.py/gen_3d.py CLI + README.md)。任何会话照 README 即可调用:生图(POST /v1/images/generations)、3D 提交(POST /v1/api/3d/submit)、3D 查询(POST /v1/api/3d/query,必须带 model)、完成后 data[].url 下载 glb。性价比档位:3D 默认 hy-3d-3.0(草稿 express,高配 3.1 除非必要不用);生图默认 seedream-image-v5.0-lite。生图需用户在控制台开"在线推理服务"后付费才可用(401007);key 建议用户重建专属后替换 config.json。
- **专家团架构(2026-09-05 用户拍板)**:三层结构——总监层(制作人=用户终审/技术总监=编排 deepseek)、专家层(主策划/美术总监/资产生成/程序主程=GLM-5.3/音效)、验收层(视觉=hy-vision-2.0-instruct/测试=断言体系)。角色卡在 `docs/team/agents/`,编排协议+验收矩阵+分权红线在 `docs/team/PROTOCOL.md`,花名册在 `docs/team/ROSTER.md`。每个专家=模型+角色卡;待接入模型由用户提供 API 后登记。**总监不写业务代码,代码全交 GLM-5.3;验收只查输入对输出对(黑盒),不审实现;审美/手感终审永远在用户。**
1. 提示词在 `D:\DevTools\IronCurtain\docs\prompts\m<X>.<Y>-*.md`(六段式:GDD 摘录/接口契约/现状/硬验收/输出格式)
2. `python glm_call.py <prompt> -o <out>` → 解析 `// FILE: <path>` 标记落盘到 Assets
3. 编辑器自动编译(或 batchmode)→ 读 `C:\Users\HP\AppData\Local\Unity\Editor\Editor.log` 找 `error CS`
4. 有错:错误文本回喂 GLM-5.3 修复(≤3 轮),简单错自行修
5. 编译绿 → EditMode 断言(-batchmode -runTests)或纯 C# 测试 → 截图 → vision 验收 → 用户审
6. 每模块绿后更新本文件「进度」节

## 6. 架构约定
- asmdef 三层:IronCurtain.Sim(noEngineReferences,纯 C# 确定性)/ IronCurtain.View(Unity)/ IronCurtain.Tests
- Sim 层:固定步长(10Hz tick 概念,调用方驱动 Step(),不碰 Time);实体注册表;命令缓冲;种子 RNG;事件流
- 数值:Resources/gameconfig.json 进 Sim 的 POCO(Sim 自持解析,View 负责读文件喂字符串,Sim 不依赖 UnityEngine)
- 命名空间 `IronCurtain.Sim` / `IronCurtain.View`
- 单文件 ≤300 行;Sim 内禁用 UnityEngine/Mathf(用自建结构)

## 7. 进度
- [x] 2026-09-05 M6.5 椭圆大地图(Sim)✅ **135/135 全绿**:椭圆 286×30 包围盒(长半轴143/短半轴15)、河宽10(x∈[138,148))、上下桥 5 格、中央岛 8×8、涉水×0.5/桥正常、椭圆边界 Clamp、建筑布局(蓝Base15/兵营46/资源点77/箭塔108/中立岛143/红箭塔178/红资源点209/红兵营240/红Base271,全部 Row15)、rally(15,270)/(15,16)。见 `大地图改造拆解.md`。
- [x] 2026-09-05 GLM 幻想 API 事故与恢复:GLM-5.3 修改类任务违反协议(凭空编造 Entity.InGarrison 等 API 重写 8 个 Sim 文件),编排者漏跑 api_gate 放大事故;已从上下文重建全部 8 文件并带地形适配,135 测试全绿。**教训入协议:修改类任务强制内联现状全文+强制跑 api_gate+落盘后核对文件行数**。
- [x] 2026-09-05 TokenHub 美术管线(team-pipeline)+ 专家团(ROSTER/PROTOCOL/9 角色卡)+ 视觉验收换 hy-vision-2.0-instruct(flash 弃用,描述质量不足)
- [~] M6.6 View 椭圆地形渲染:结构验收通过(编译绿/135 测试绿/离屏截图 4 张),hy-vision 视觉验收 3 轮修复后主要客观项通过(河蓝可见/虚空暗/草地散点/桥岛/建筑×2.5 阵营 tint+旗帜/单位 2.4m 可见);**审美终审待用户**(待看 D:/DevTools/shots/m6.6_*.png;vision 对"石路纹理感/建筑高瘦感/草地占比"仍有主观性 FAIL 描述,shot1 左尖端构图偏空)。**新增 CaptureBatch 验收通道**(batchmode 离屏渲染截图,全程不抢前台,见 §8)
- [x] 2026-09-05 M6.7 兵营总览面板 ✅ **145/145 全绿**(135+10 新):BarracksOverviewModel(纯逻辑,无 UnityEngine)+BarracksOverviewPanel(IMGUI)+GameBootstrap 接线+Tests asmdef 加 View 引用。边界态:被摧毁→区块红字"已摧毁 · 不可生产";被占领→Side 翻转自动从总览消失;多兵营自动多区块;3 槽进度/等位/取消/6 英雄按钮/人口。**IMGUI 不入离屏截图,面板视觉请用户在编辑器 Play 时查看。** GLM 本轮 4 处小错(缺 using/light.rotation/cfg.Game→cfg.Production/残留围栏)已由编排者自修;parse_files.py 已加围栏过滤根治。
- [x] 2026-09-05 **M8.1 建筑多格占用 ✅ 150/150**(将兵化专项第一批,拆解见 `docs/将兵化改造拆解.md`):BuildingFootprint(占地表:基地5×5/箭塔4×4/兵营5×4/资源点4×4/堡垒3×3,米制半宽半高、中心锚定、配置化)+矩形碰撞推出(幂等)+出生点贴前缘+城门格(D18 敌缘+0.6)+攻城前排+视野标记占地化(建筑视野圆盘恢复且不过滤椭圆)。**过程中修复 3 个 Sim 真 bug**:①FindGateCell 敌侧方向反了(蓝建筑门开在自家后方,目标落在占地矩形内不可达);②溃逃起点落在失守建筑占地内(推出+软碰撞边缘振荡);③多溃逃单位同门堆叠(软碰撞 0.9m>抵达阈值 0.15m 永远到不了)→溃逃单位分派不同城门格。诊断手段:临时诊断测试+TestContext 输出定位(已删)。
- [x] 2026-09-05 **M8.2 兵团底座 ✅ 157/157**(155+2):Entity 加 IsSoldier/GeneralId/FormationSlot;RegimentSystem(SpawnGeneral 满编 20 兵/4×5 阵型跟随/蓝面(1,0)红面(-1,0)/主将死士兵无将续战就近逐杀至死/同兵团软碰撞豁免);士兵平伤(任一为士兵→攻击方 Attack 原值,无英雄克制规则);士兵视野近战 3/远程 6;AI 不指挥士兵。编排者修正 GLM 自写 T7 两处(手摆士兵未冻结被阵型拉回、视野 3s 保鲜期未过期)。
- [x] 2026-09-05 **M8.3 驻军/交换士兵口径 ✅ 158/158**:仅士兵可驻军(主将城外);容量 30/30(兵营/箭塔/资源点,基地 0,堡垒 3);临时驻军=城后等候(TempGarrisonId 登记不冻结移动,主将死/距城>15m 自动解除=截击调遣的自然结果);交换 1:1 兵/次、攻方主将免疫;易主/废墟士兵只清登记不溃逃;占领后幸存士兵自动驻军。**编排者接管 5 红**(GLM 修复轮超时+输出失格满 3 轮):资源点容量 0→30、AI 防守改调兵站前(士兵驻军防御留 M8.5)、DestroyTests T6 士兵化重写、SiegeTests T4 矛盾断言清理、临时满员 30 兵。
- [x] 2026-09-06 **M8.4 补兵池+主基地产将+名额制 ✅ 158/158**(M8.4a/b/c + 并行 M8.6a View 兵团渲染):ReinforceSystem(池 60、2s/兵、SJF 最缺先补、交战排除脱战 3s、变装=主将 Hero、槽位唯一);主基地产将 20s(镇岳迅捷 7.5s 保留)、名额 4(测试版;正式 8)、产将自带满编 20 兵、出生点列螺旋外扩(避紧邻建筑)+兵团整体出生校验(阵型不压建筑/不贴单位);MatchSetup 开局 3 将(磐石/断峰/坠日,红蓝对称);AI 产将(简单满产/普通配比);总览面板改版(主基地将生产区块+补兵池显示+补兵按钮);EntityView 士兵占位小体+主将"兵力 X/20"标签。**编排者修复**:出生距阵型纵深让 4 格、列螺旋、死亡单位不挡出生、单位出生默认脱战(SecondsSinceCombat=99)、多个测试节奏账(20s 产速+错峰出生)与场景隔离。
- [x] 2026-09-06 **M8.4d 回归修复 ✅ 160/160**(GLM-5.3 接任主编排后首单,一轮过):①资源点驻军 30→**0**(撤销 deepseek 越权改动,恢复原设计"资源点不可驻军";中立点占领后士兵不再自动驻入,T1 语义翻转);②**士兵不继承英雄被动**(HeroAbilitySystem 三处守卫:攻击附带被动任一方士兵即跳过/磐石士兵不回盾/士兵不构成逐风冲锋阻断源;新增 T8 攻击无被动、T9 士兵不回盾+主将对照 2 条断言)。glm_call 默认 max_tokens 已提 64000(本单起)。
- **主编排已换 GLM-5.3**(2026-09-06 用户拍板,deepseek 有降智迹象):代码与编排同模型,接口契约零转译损耗;执行原则=GLM 用量需有效益+编排者非必要不下场。
- [x] 2026-09-06 **M8.5a 射程体系 ✅ 160/160**:近战(磐石/镇岳/逐风)=2、断峰=3、射手(坠日/星落)=9;建筑 Range 未动;**TestUtil.RunUntil 测试基建落地**(Combat/Destroy 已用,新测试一律用它,节奏账根治)。测试几何适配 2 处(射程变长改变索敌边界:T3 红镇岳隔行、T6 改镇岳贴塔拆除)。
- [x] 2026-09-06 **M8.5b 兵团自主行为层 ✅ 166/166**:RegimentBehaviorSystem(SimConfig.AutoBehavior 开关,默认 false,MatchSetup 开;手动命令永不覆盖;索敌进攻(兵团视野:主将视野表+士兵 3/6)>支援(友军主将 15 格内交战);手动打断写 Sup 防复读,新事件可再触发)+AI 每 5s 自动补兵(池≥10)+**补兵槽位防崩**(出生格被占则沿槽向外推 1m×3 次,再堵跳过不耗池——修复路过建筑旁补兵抛异常)。行为测试补 TearDown 复位静态系统(修复跨测试污染)。
- [x] 2026-09-06 **M8.5c 自动布阵规划器 ✅ 172/172**:RegimentFormationSystem(HeroClass 三分类;PlanFormation:质心→框选内视野找敌→朝向(敌质心/敌大本营/前进向)→常规三排(前重装/中战士/后射手,横距 6 排距 6)或屏风模式(方位差 >120°:射手居质心,近战按敌簇分向弧半径 4);Apply=设 FacingOverride+手动级移动);FacingOf 升级(规则③:后撤/横移不转身,面朝敌方向;FacingOverride 最优先)。Entity 加 FacingOverride。View 接线(T 键/框选)在 M8.6。编排者修:GLM 测试文件路径写错(门禁拦截后归位)、HeroCfg.Vision 假字段(对齐视野口径)、2 处测试布局(阵型压格/对向穿廊死锁)。
- [x] 2026-09-06 **M8.6 View 指挥大块 ✅ 179/179**(GLM-5.3 一版过+编排者 3 小修):①**补兵行军表现**(纯表现不可截击):Entity.MarchOrigin(出生源)+ReinforceSystem.MarchSource(距主将最近己方存活非废墟兵营→无兵营 fallback 主基地→再无 null 即时落位)+ReinforceMarchView(View 组件:士兵模型自生产点以 MoveSpeed 走向活目标 e.PosF,<0.5m 入队交接;EntityView.MarchHold 跳过位置同步);②**点选/框选指挥**(SelectionSystem,新):左键点选(3m 命中,士兵点击映射其主将)/拖框选蓝方主将、右键手动级移动(SetMoveTarget manual:true,含防拉回语义)、绿色选中圈、点击标记、BarracksOverviewPanel.LastPanelRect 避让;③**T 键自动布阵接线**:选中≥2 兵团→屏幕中央"按 T 键自动布阵"提示 2.5s 渐隐,T=RegimentFormationSystem.Apply;④**建筑占地底板**:EntityView.BuildBuilding 按 BuildingFootprint.HalfW/H 画阵营色薄板(多格占地可视化)。新增 T7(最近兵营)/T8(fallback 主基)/T9(空场 null)行军源测试。**编排者自修 3 处**:提示文字每帧重置永不满淡(→仅选择变化瞬间重置)、SelectionRings 空根节点泄漏、不透明材质下点击标记 alpha 淡出无效(加缩放消退兜底);gitignore TestResults 目录规则修正(原 `TestResults*.xml` 不匹配子目录文件)。**api_gate 符号库缺口备案**:`const` 成员/Unity View 类型/Tuple.Item/注释内版本号共 15 模式全误报(330 条),人工 triage 无幻想 API 后放行——下次 Sim 大改前值得给 api_gate 补 const 提取与 View 白名单。**视觉验收待用户编辑器 Play 审**(框选手感/行军观感/占地底板/提示文字)。
- [x] 2026-09-06 **M8.5d 卡死自动绕行 ✅ 176/176**(用户拍板:卡死 1.5s 自动寻路绕开):NavigationAid(前向探测 1.2m 找阻挡建筑→外扩 1.5m 矩形较近角为旁路点;单位互堵→垂直让位 2m 按 id 奇偶定侧);主将(MoveUnits)/士兵(RegimentSystem)/溃逃同路径接入;停滞判定=位移<期望步长×0.25 连续 15 tick;旁路 0.8m 清障/45 tick 超时保险。**编排者修 1 个顺序 bug**:GLM 把 PrevPosF 采样写在移动后,检查时距离恒 0→全员假卡死全员绕路(诊断测试定位);BuildingRectPush 断言随新语义更新(贴边滑动/绕行合法,穿透>0.05 违规)。
- [x] 2026-09-06 **M8.7 撤退 13c ✅ 188/188**(GLM-5.3 一版过+编排者修 4 处幻想 API):①**RetreatSystem.OrderRetreat**(玩家授意撤退:最近己方非废墟据点、站定点=建筑边缘外 1.2m、兵团原子=同兵团非驻军士兵同步 Routing、清自动决策);②**溃逃系数统一 −0.3/+0.3 → −0.25/+0.25**(SimCore 4 处含断峰扇形分支;"撤退期间血量与攻击力−25%"按乘区近似:输出−25%/承伤+25%,撤退结束自动消失——待确认①按此落地);③**防拉回 RetreatHold**:OrderRetreat 置位→RegimentBehaviorSystem 跳过(索敌/支援均压制,被迫接战保留),玩家 manual SetMoveTarget 解除→自动层恢复接管;④士兵撤退态自动复位 2 处(主将到站/被打断、无将续战清除);⑤**View 撤退按钮**(SelectionSystem:选中≥1 主将底部中央"撤退"按钮,MouseOverPanel 防点击穿透)。新增 RetreatTests T1-T9(基本/输出比 0.75/承伤比 1.25/到达站定/防拉回+解除/撤退中不覆盖/士兵复位/边界×4/最近据点)。**编排者修 GLM 4 处幻想 API**:GameConfig.LoadDefault→GameConfigParser.FromJson、ClearEventListeners→TearDown 四系统复位、SimConfig{TickRate=30}→默认 10Hz(防节奏崩)、SimEvent.Id→SubjectId(属性链漏检);**新坑根治**:GLM 末块尾部无围栏 markdown 摘要被 parse_files 写入 cs 尾部→parse_files.py 加行首 `**` 截断(.cs,自测过)。api_gate 117 条全为符号库缺口误报(m8.7.allowed.txt 备案)。**M8.6+M8.7 View 视觉验收待用户编辑器 Play 审**(撤退按钮+框选/行军/底板/提示文字一并)。
- [x] 2026-09-06 **deepseek 挑刺岗盲测 + M8.7fix ✅ 188/188**(流程实验):通道打通=ZCode config 里已有 deepseek provider(`api.deepseek.com/anthropic`,deepseek-v4-pro;**v4-pro 默认开思考会吃光 max_tokens,须 thinking disabled**);新工具 `glm-pipeline/deepseek_review.py`(挑刺 runner,泛化任务单内嵌)+`review_check.py`(裁决硬门禁:报告+「## 裁决」段齐才放行落盘)。盲测=挑 M8.7 原始输出:严格分 1/5 未过线(漏检 4 处全为 API 存在性类=api_gate 强项),但计划外 3 真刺全中机械门禁盲区——**修复 2 个真 bug:①撤退按钮 Rect 残留(清空选择后旧按钮区域左键永久失效→拦截加选中守卫)②交换中士兵被误置撤退态(→Exchanging 排除)**;误报 2/24 零顺着说,成本 ≈¥0.55/次。**待用户拍板**:窄口径上岗(逻辑/时序/状态类审查岗,嵌入"GLM 输出落盘前+提示词定稿前"两节点)或按盲测规则换 Kimi(K2.5,¥27 全程)。→ **2026-09-06 制作人拍板:deepseek 挑刺岗常驻(窄口径)**,已入 PROTOCOL 验收矩阵与 ROSTER(LogicAuditor);Kimi K3(非 K2.5,2.8T/7 月发布)列为总编排首选候选,档位建议 Moderato ¥99 起(详见模型管线表)。
- [x] 2026-09-06 **M6.1 镜头/迷雾/小地图 ✅ 198/198**(GLM 一版过三门禁+编排者自修;提示词 `docs/prompts/m6.1-camera-fog-minimap.md`):①**CameraRig(D18)** 鼠标贴边(12px)匀速平移 28m/s 公开可配置、焦点钳制包围盒 6m 余量、悬停 IMGUI 面板停滚、开局聚焦 (40,15);CaptureRunner 截图改走 rig.SetFocus 防打架;②**FogView(D34)** 286×30 纹理贴 y=0.25 地面四边形(URP Unlit 透明配方),IsFresh→alpha 0/160 双线性平滑,0.5s 刷新+CaptureBatch 手动 RefreshNow;**敌实体信息过滤**:敌露天单位不可见=模型/血条/兵力标签全隐;敌建筑未见过=全隐、失视=冻结快照(血条冻结+驻军点=LastKnownGarrison+归侧按快照),Sim 纯增量 `VisionSystem.LastKnownBuilding`;③**MinimapPanel(D19)** 右下角 440×90(点采样背景:椭圆/河/桥/岛/虚空),雾叠加(纹理垂直翻转:GUI v=0=底部=南)、己方全量+敌方仅可见/快照、相机取景白框(4 屏角射线投地面)、点击/拖动=SetFocus+防穿透,SelectionSystem 守卫加小地图 Rect;MinimapLayout/FogRules 纯逻辑可测。新增 MinimapFogTests 9 条。**编排者自修 5 处**:FogView 幻想 API ViewAssets.CreateMesh(Vector3[],uv,trys) 重载→自建 Mesh;FogView 枚举未限定(Bilinear/Clamp);小地图背景+雾上下颠倒(GUI 纹理 v=0=rect 底部=南与 row0=北相反→背景行序翻转+DrawTextureWithTexCoords 垂直翻转);T4 出生点越界(300→250);**MatchSetup 潜伏崩溃修复**(M8.4d 起 MatchSetup.Build 零覆盖:开局 3 将位差 2 列,4×5 士兵阵型块大面积压叠→SpawnEntity 抛"出生点过近";CaptureBatch 是 M8.4 后首跑当场暴露):SpawnSoldier 加出生防崩(沿槽外推 1m×3 次,口径=格心判定与 SlotCellFree 同构;首版误用落点判定与 SpawnEntity 格心口径不一致致 AiTests T4 红,对齐后绿)+开局将位重排(蓝 24/30/36、红 263/257/251,将间距 ≥6 列=阵型块互不压叠且避基地/兵营占地)+SimSmokeTests 补 MatchSetup 冒烟(6 将 120 兵)。api_gate 25 条全为符号库缺口误报(m6.1.allowed.txt)。**hy-vision 客观项过**:雾罩未探区/己方清晰/平滑渐变/无渲染错;30s 中线雾封 vs 60s 随军清晰=视野联动正确;敌区无任何单位建筑细节=过滤生效。截图 `D:/DevTools/shots/m6.1_1..4_*.png`(旧 m6.6 备份 backup_m66/);**小地图/雾审美+镜头手感终审待用户编辑器 Play 审**(IMGUI 不入离屏截图,同总览面板惯例)。
- [ ] M6.2 战斗 UI / M6.4 特效音效 / 计时加时积分结算
- [ ] 美术风格定稿+3D 批量生成(管线已就绪,等风格拍板)
- [ ] GPT 总编排交接准备(用户拟接入 GPT-6 Astra/5.6 Sol 任总编排;交接载体=本文件+docs/team/+team-pipeline README)


- [x] 环境全通:引擎/Unity MCP/vision-eyes/GLM-5.3 API 直连
- [x] 计划批准(v1.1,设计变更 #1)
- [x] M0.1 分层骨架+Sim 核心
- [x] M0.2 数值配置 JSON
- [x] M0.3 确定性验证
- [x] M1.x 战斗核心 ✅ 42/42 断言全绿(移动/伤害公式/六英雄机制/克制链)
- [x] M2 生产与资源 ✅ 52/52 全绿(GDD 55人口110秒断言通过)

- [x] M3.1 占领核心 ✅ 62/62(意图存建筑/封城锁/交换1:1与1:2/增援/自动驻军)
- [x] M3.2 箭塔战斗 ✅ 70/70(溅射半径1满9+1/驻军强化+2封顶12/修正系数)
- [x] M3.3 驻军完整 83/83(含 2026-09-04 用户新规修订:废除10秒计时/正式优先覆盖GDD2.4.1/易主溃逃/满员路过站定)
- [x] M3.4 摧毁废墟重建 93/93(拆塔节奏/冲锋首击5/废墟化+守军后撤/D26优先级/中立免疫/重建归重建者/大本营判负)
- [x] M4.1 升级被动 102/102(2EP一次性/磐石免疫/镇岳迅捷2.2+7.5s产速+D11衰减/断峰破军扇形/飞掠拆塔7/s不锁塔/诛心脆弱/诛灭6+1%)
- [x] D28 伤害叠算公式拍板并落地 102/102:基础×(1+Σ输出增幅)×(1+Σ承伤增幅)×∏(1−免伤)+附加项;增益加算/免伤乘算。规范文档 `伤害叠算公式.md`(重构 GDD 引用)
- [x] M4.2 海克斯+道具 112/112(8 卡全效果/节点抽取 3/5/7/9 分钟/三选一/后选覆盖/已用-50%/堡垒/攻城锥/临时阵地;D29-D33 已入档)
- [x] M5.0 视野/迷雾 VisionSystem 120/120(近战6/射手9/断峰鹰眼10/塔12→**24**/大本营20→**40**/其他10→**20**[2026-09-06 扩大一倍];精灵每30s扫掠首发作第30秒;信息3s保鲜/据点冻结/露天消失;双方对称纯信息层)
- [x] M5 AI 简单+普通 130/130(决策 2s 节奏/简单满产+被动回防/普通集结≥8 波次+挑弱侧+占中立优先+主动防守+规律抽卡+镇岳配比+受挫撤退;只读 VisionSystem)
- [x] 2026-09-05 连续坐标大改造 ✅ **130/130 全绿**(SimVec2/6×255/建筑碰撞圆/软碰撞/欧氏索敌/全系统米制适配;交换规则修复见变更#5)
- [x] 2026-09-05 URP 17.0.3 管线升级(manifest+UrpSetup.cs+View asmdef 引用 Core/Universal Runtime)
- [x] M6.0c Kenney 卡通资产绑定(编译绿+截图验收:城堡塔/风车/角色/草地画风确认可用;细节:光环盘已改亮、天空0x79b5ea、草地棋盘)
- [ ] M6.0d 六英雄模型替换(等 Quaternius 机甲,用户下载→Assets/Models/)
- [ ] M6.1 镜头边缘滑动+迷雾渲染+小地图(D18/D19/D34)
- [ ] M6.2 战斗 UI(顶栏/飘字/血条/选中/射程)
- [ ] M6.3 操作 UI(生产/海克斯/升级/道具/点选框选指挥/驻军面板 D24)
- [ ] M6.4 特效+音效 / M6.5 新手浮层 / 对局计时+加时+积分结算(Sim 无 Match 时钟,待补)
- [ ] M7 收口打包
- [ ] 新手教学/智能决策体(内容待用户定稿)
- [ ] M6 UI 表现(迷雾渲染/镜头/小地图/驻军面板/操作 UI)
- [ ] M6 UI 迷雾渲染(平安京/LOL 风)
- [ ] M6 UI 表现(交互规格 D18-D24)/ M7 收口打包
- [ ] M5 AI / M6 UI 表现 / M7 收口打包
- [ ] M4-M7(见计划文件)

## 7.6 M3.3 驻军完整契约要点(续工据此写提示词)
- 正式驻军 ≤9(配置 GarrisonFormal);临时驻军 ≤9;双满不可入(报错)
- D28(编排默认,待用户确认):行军途经己方建筑=自动临时驻军,10 秒后强制继续行军;正式有空缺则转正式
- 远程驻军依托建筑射击:以建筑格为原点,射程=单位自身射程,间隔=单位间隔,走统一伤害公式
- 近战驻军出击:敌单位曼哈顿≤2 时出城迎击(城门格)
- 交换消耗顺序:敌方优先消耗临时驻军(SiegeSystem.KillDefenders 需改,编排者负责)
- D24 Exit API:GarrionSystem.Exit(sim, buildingId, HeroId, out error) 每次放一只到城门 Idle
- D26 攻击优先级:单位优先于建筑(单位自动索敌+M3.4 摧毁循环都要遵守)
- D27 建筑回复:非大本营,SecondsSinceCombat≥20 后 4/s 回血(建筑自身 tick 由 GarrisonSystem 维护)
- 撤退(废墟后撤-30%)挂 M3.4,由 GarrisonSystem 提供 EvacuateAll(building, retreatDebuff)

## 7.5 归因台账(新分工自 M2 起,每模块更新)
| 模块 | GLM 交付 | 编排者接管 | 结果 |
|---|---|---|---|
| M2 | ProductionSet(终稿✓)+ProductionSystem 首轮(架构✓2引擎bug)+测试首轮(4测试bug) | 修复轮退化弃用→接管 ProductionSystem+全部测试 | 52/52 ✓,接管触发条件=连续2轮失败(按约定执行) |
| M1.x | M0.1/M0.2 主体 | 内核与测试 | 42/42 ✓ |
| M2 | ProductionSet✓+首轮架构 | 修复轮退化弃用→接管 | 52/52 ✓ |
| M3.1 | SiegeSystem 架构+10 测试(首轮过门禁) | 3 引擎精确修(重复入城/幽灵占格/翻侧找位)+3 测试修 | 62/62 ✓ |
| M3.2 | TowerCombatSystem 架构+8 测试场景 | 引擎 3 API 修(提示词表格歧义 1 处我方责任/GridPos.X-Y 漂移/字段名)+0.1f 容差;测试基建重写(保留场景) | 70/70 ✓ |
| M6.6 | 5 文件一版命中全部规格数值(相机/分块/颜色/尺寸) | 3 轮修复(前两轮是我方规格缺陷:线性 hash 斜纹/虚空盖河;第3轮根因定向探测后一次命中);编排者小修:残留围栏×2、下桥栏杆 0.5 偏移;**新建 CaptureBatch 离屏截图通道**(后台不抢焦点) | 编译✓ 135/135✓ 视觉客观项过,审美待用户 |
| M6.7 | 5 文件+10 测试一版交付,架构/语义全对 | 编排者 4 处小修:缺 using IronCurtain.Sim、light.rotation→go.transform.rotation、cfg.Game.Production→cfg.Production、残留围栏(parse_files.py 已加围栏过滤根治);asmdef 重排版(语义保真)接受 | **145/145 ✓** |
| M8.1 | 占地表/矩形碰撞/出生点/城门/攻城前排/视野 两批交付,数值全命中;2 个自写测试有小错(重复spawn/NewSim缺config) | 编排者修复 3 个 Sim 真 bug(城门方向反/溃逃起点入占地/多单位同门堆叠→分派不同城门格)+测试适配;诊断测试定位法已沉淀 | **150/150 ✓** |
| M8.2 | 兵团底座两批一版过(Entity 3 字段/RegimentSystem/SimCore 集成/平伤/视野 3·6/AI 守卫) | T7 两处自修(冻结移速防阵型拉回、视野 3s 保鲜步进 35 tick) | **157/157 ✓** |
| M8.3 | 口径改造一版过+测试重写(158 条) | 修复轮 GLM 超时+输出失格(无 FILE 标记/截断)满 3 轮→接管 5 红:资源点容量 0→30、AI 防守调兵容错、3 个测试修正 | **158/158 ✓** |
| M8.4+M8.6a | 4a/6a 并行双交付一版过(补兵池+兵团渲染);4b 产将一版过;4c 面板一版过(自写 T9 场景 1 处红) | 编排者修复:出生螺旋+阵型纵深校验、死亡实体不挡出生、出生默认脱战、迅捷 7.5s 保留、节奏账适配(20s+错峰);GLM 漏改测试 1 处(SideOf 假 API) | **158/158 ✓** |
| M8.6 | 9 文件一版过门禁全清(api_gate 330 条全为符号库缺口误报,人工 triage 放行;static/rewrite 一次过),行军源/选择/布阵接线/占地底板数值全命中,新测试 T7-T9 一次绿 | 编排者 3 小修:提示淡出计时重置时机、选中圈根节点泄漏、点击标记缩放兜底;gitignore 修正 | **179/179 ✓** |
| M8.7 | 7 文件一版过门禁(static/rewrite 一次过;api_gate 117 条全为符号库缺口误报,备案 m8.7.allowed.txt),RetreatSystem/系数/防拉回/撤退按钮全命中契约,RetreatTests 输出两版自取修订版一次绿 | 编排者修 4 处幻想 API(GameConfig.LoadDefault/ClearEventListeners/SimConfig{TickRate=30}/SimEvent.Id→SubjectId)+SelectionSystem 尾部 markdown 污染清除;parse_files.py 加行首 `**` 截断根治(自测过) | **188/188 ✓** |
| M6.1 | 13 文件(5 新+8 改)一版过三门禁(api_gate 25 条全误报;static/rewrite 一次过),CameraRig/FogView/FogRules/MinimapPanel/MinimapLayout 数值与契约全命中,测试 9 条一版绿 | 编排者修 5 处:FogView 幻想重载 CreateMesh(Vector3[],uv,tris)/AttachMesh 参数序→自建 Mesh;FogView 枚举未限定 2 编译错;小地图纹理上下颠倒(GUI v=0=南)×2 处翻转;T4 越界坐标;**MatchSetup 潜伏崩溃**(零覆盖+将位压叠)——SpawnSoldier 防崩(格心口径,首版落点口径致 T4 红返工一轮)+将位重排+冒烟测试 | **198/198 ✓** |

## 7.75 三层门禁(已上线)
1.  静态门禁:命名空间白名单/Sim 层禁 UnityEngine/重复类型/FILE 白名单
2.  重写检测:改动>70% 打回(修改类任务)
3.  API 门禁:未知类型构造/枚举成员/已知类型成员对照拦截(注释剥离+属性链白名单)
执行顺序:GLM 输出 → api_gate → static_gate → rewrite_detector(修改类)→ parse_files → 编译 → 测试
GLM 输出落盘前先跑:python D:/DevTools/glm-pipeline/static_gate.py <输出文件>
检查:命名空间白名单(IronCurtain.Sim)/Sim 层禁 UnityEngine/重复类型定义/FILE 路径白名单。5ms 拦截退化重写类输出,通过后再 parse_files.py。

## 8a. 版本库(2026-09-06 起)
- 项目根 `D:\DevTools\IronCurtain` 已 git init(main 分支,tag m8.4d=首个基线)。
- **约定:全绿即 commit**(每个模块/修复轮测试全绿后立刻提交,信息注明模块与测试数);GLM 落盘后、测试未绿前**禁止 commit**;事故恢复=`git checkout -- <文件>` 或 `git reset --hard`(以最后一次全绿 commit 为锚)。
- .gitignore 已排除 Library/Temp/日志/TestResults;Assets(含 .meta)与 docs 全部入库。

## 8. 已知坑(务必遵守)
- cmd 管道喂中文给 python 会变锟斤拷 → 中文一律走文件,脚本读文件
- cmd 不支持 heredoc/单行多引号 python → 写 .py 文件执行
- Unity `file:` 包依赖路径**相对 Packages 目录**(../../unity-mcp-main/MCPForUnity)
- 包解析成功≠编译成功:若 Library/ScriptAssemblies 缺 DLL,删 Library 重导
- 国内网络:GitHub ~28KB/s(codeload 用 16 线程 chunkdl.py 提速);unitychina CDN 只到 6000.0.30f1;Epic/虚幻网络不可达;**Google Drive 不可达**(Quaternius 资产走 Drive,需用户加速器下载);kenney.nl 可达(~22KB/s);itch.io 超时
- Unity Hub 是 MSIX,在 WindowsApps,更新后快捷方式路径会变
- vision 调用 43s→11s 的关键:图片缩到 1600px JPEG + thinking disabled
- **Unity 许可证**:直接启动 Unity.exe 会"No ULF license found"退出 → 必须先启动 Unity Hub(它会拉起 LicensingClient),再启 Unity;Hub 未登录但 LicensingClient 会给 personal serial
- **M6 表现层工作流(重要)**:截图验收=编辑器菜单 IronCurtain/Play & Capture(热键 Ctrl+Shift+Alt+P)→ 场景内 CaptureRunner 按 sim 10/30/60/120s 拍 4 张到 D:/DevTools/shots/(Speed=3x,蓝方配 Normal AI 对打);触发热键用 PowerShell SetForegroundWindow+SendKeys(见 workspace/win_focus.ps1);**Play 运行期间禁止改脚本**(会热重载丢 Sim 字段刷 NRE,GameBootstrap 已有防护但勿依赖)
- CUA(电脑控制)与 Unity 交互时频繁断连(broker 超时),Unity 编译/导入期间不可用;只读操作在空闲时可用
- **GLM 末 FILE 块尾部外溢 markdown 摘要(M8.7 事故)**:无围栏包裹的"改动摘要"曾被 parse_files 原样写入 cs 尾部→已根治(parse_files.py 对 .cs 内容遇行首 `**` 截断,自测通过);落盘后仍建议 `git diff` 核对尾部
- 外部写 .cs 新文件后 Unity 可能不自动编译 → 前台激活编辑器+SendKeys ^r 强制刷新;或 kill 编辑器走 batchmode 编译
- **glm_call.py -o 输出路径的父目录必须已存在**(不会自动建目录;M6.1 首跑 -o out/xx.md → 生成 12 分钟 75k tokens 全部丢失只剩 FileNotFoundError,重烧一轮)。惯例:输出直接放 glm-pipeline/ 根(m6.x.out.md),用前 ls 确认
- **IMGUI 纹理上下颠倒坑**:GUI.DrawTexture 把纹理 v=0(=SetPixels32 第 0 行)画在 rect 底部;Sim row0=北=顶部 → 直接画必颠倒。修法:像素行序翻转 或 GUI.DrawTextureWithTexCoords(rect, tex, Rect(0,1,1,-1)) 垂直翻转(MinimapPanel 已两处落地)
- Sim 连续坐标规则速查:`SimVec2{X=列米,Y=行米}`;`Entity.PosF`=单位权威位置;`Entity.Pos`=格(建筑);世界坐标 x=X,z=3−Y;碰撞半径 `SimCore.BuildingCollisionRadius`;单位不占格;测试模板布局(6×255 下):兵营(1,8)/蓝塔(1,16)/红塔(1,24)/蓝基地(1,0)
