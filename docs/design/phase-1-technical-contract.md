# 首期多组织基础：详细技术设计与接口契约

版本：0.2；日期：2026-10-23；状态：**技术提案，待审阅，未实施**。

依据：Qifei-L/Books 的 docs/requirements-review，基准提交 480cd008223dd311093ea16ab8b38e640026e41f；[Draft PR #1](https://github.com/Qifei-L/Books/pull/1)。本文承接多组织权限设计，不替代已经确认的业务规则。本文的参数、技术栈及路径均为建议，不表示用户已经批准实施或部署。

## 1. 结论与实施前关口

建议采用 **Go 模块化单体后端 + PostgreSQL + 标准外部 OIDC/OAuth 身份服务**。HTTP API 和 MCP 只是两种适配入口，共用应用服务、授权策略、事务和数据访问；不拆微服务，不引入 Redis、消息队列、独立权限引擎或自制 OAuth 授权服务器。

身份服务的明确首选提案为 **Keycloak**，先在隔离测试环境验证，再决定实际采用。理由是用户注册、验证、登录、恢复及 OAuth 由成熟产品承担，Books 专注组织权限；代价是多一个服务及其数据库、升级、备份、邮件配置和安全维护。对小项目这可能偏重，不能包装成零成本依赖。若已有合适的组织身份平台，应优先评估复用，但不要在没有确认的情况下增加付费账号或创建新服务。

真正需要用户审阅的关口：
1. 同意本首期范围、推荐栈及 Keycloak 验证方向；身份供应商与运维承担者仍待选定。
2. 技术文档审阅后，**另外明确批准开始编码**；本次“继续技术设计”不构成编码批准。
3. 生产供应商、区域、费用、域名、备份和发布计划另行批准；不随技术栈一起默认批准。

编码批准后先做认证兼容性小验证。Keycloak 与目标 MCP 客户端的 resource 参数、受众、发现及注册方式尚未实际测试；验证不通过就报告差距并修订方案，不能降级为共享管理员令牌。

## 2. 首期范围与已经确认的规则

首期交付边界：用户身份映射；组织；成员；公司与一对一账套的基础记录；邀请；账套授权；审计；HTTP/MCP 契约和自动化测试。不实现科目、分录、凭证、余额、报表或会计引擎。Web 产品界面暂停；身份平台自带登录页不等于开发 Books Web。

已确认业务：
- 已注册且验证身份的用户可以创建组织并成为首位管理员；一个账号可以加入多个组织，各组织独立授权。
- 一个组织可以管理多个公司，每家公司一个独立账套；组织不直接记账、不自动汇总。
- 管理员管理本组织全部账套、成员和授权；普通成员按账套获得只读或可编辑权限，不可转授权。
- 始终保留至少一位管理员；最后管理员退出前必须先交接。
- 用户主动接受邀请才加入；邀请七天有效、可撤销，过期重新邀请；邀请人失去管理员权限，其未接受邀请失效。
- 移除后重新加入、管理员降为普通成员，都需明确新授权，不恢复旧权限。
- 所有入口包括 MCP 都由同一后端验证身份、资源归属、权限和业务规则。

本位币、会计年度、业务时区及会计引擎仍未确定。首期账套字段只含身份与显示信息，并返回 accounting_setup_status=not_configured；它是组织权限基础记录，不是可记账账套。不能偷偷填 USD、自然年或 UTC 作为业务默认值。系统事件时间使用 UTC，不代表会计业务时区已经确定。

## 3. 推荐技术栈与模块

| 部分 | 推荐 | 边界与理由 |
| --- | --- | --- |
| 运行时 | Go 1.27 系列，实施时锁定经确认的安全补丁 | 官方页面在本次检查列出 1.27.3；不使用浮动 latest。标准库 net/http、encoding/json、slog、testing 为主。 |
| 数据库 | PostgreSQL 18 系列，实施时锁定补丁/镜像摘要 | 本次官方版本表为 18.6；事务、复合外键、行锁适合租户隔离及并发授权。不要选候选发布版。 |
| SQL 驱动 | pgx v5 | 显式参数化 SQL 与事务，少量清晰 repository；首期不引入 ORM。精确依赖版本在实施时核对兼容性并固定。 |
| 数据迁移 | golang-migrate v4 的版本化 SQL | 与应用进程启动分离执行，不让每个实例自动抢跑迁移；首期只设计迁移，不运行。 |
| 认证 | Keycloak 候选 + 标准 OIDC/OAuth 库 | 不存 Books 密码；OIDC 客户端与 API access-token 验证是不同职责，不能拿 ID token 当 API 凭证。 |
| MCP | 官方 modelcontextprotocol/go-sdk | HTTP Streamable HTTP 适配，同一进程，不另设超级账号；具体 SDK 版本实施前固定并做协议兼容测试。 |
| 契约/测试 | OpenAPI 3.1、JSON Schema；Go 测试 + 真 PostgreSQL 集成测试 | 不用 SQLite 代替外键、锁和并发测试；不需要首期 UI。 |

来源：[Go 发布记录](https://go.dev/doc/devel/release)、[PostgreSQL 支持表](https://www.postgresql.org/support/versioning/)、[pgx](https://pkg.go.dev/github.com/jackc/pgx/v5)、[迁移工具](https://github.com/golang-migrate/migrate)、[官方 MCP Go SDK](https://github.com/modelcontextprotocol/go-sdk)。日期为本次查阅，不承诺未来最新；本稿未安装依赖或完成兼容验证。

建议模块：transport/http、transport/mcp、identity、organizations、companies、memberships、invitations、authorization、audit、persistence。路由解析、DTO、服务、repository 分开；授权在服务内强制执行，不能只在 HTTP middleware 执行。MCP 不能直接引用数据库 repository。所有写入应用命令都接受服务端建立的 Principal，而不是来自请求体的 actor_user_id。

## 4. 身份与认证契约

### 4.1 身份平台和 Books 各自负责什么

身份平台负责注册、邮件验证、登录、密码恢复、MFA、OAuth 同意、签发和撤销令牌。Books 负责稳定用户编号、组织成员关系和实时授权。直接测试客户端与 MCP 客户端分别注册并由受信任 token 的 client_id/azp 识别，不信任请求头自报客户端。直接 API 同样校验 token scope：本人状态 books:identity；创建组织 books:org-create；组织读/写/管理分别 books:read、books:write、books:org-admin；委托建立/撤销 books:delegations-manage 且客户端必须为批准的直接客户端。业务角色检查始终另行执行，scope 不是组织角色。

不使用邮箱自动合并身份：身份主键是 (issuer, subject)。首期只接一个经过批准的 issuer；多个登录方式绑定和改邮箱迁移另行设计，不能凭同名邮箱自动接管历史账号。

Books user 在首次有效登录后建立；注册未验证者仅可读取本人状态，不得创建组织、接受邀请或访问组织数据。已验证身份信息从受信任 issuer 的已验证 token / userinfo 取得，不信任请求体中 email_verified=true。

建议初始登录接入：测试客户端使用 Authorization Code + PKCE S256，经身份平台自带页面登录；不采用密码 grant 或 implicit grant。redirect URI 精确注册；验证 state；OIDC 登录验证 nonce。PKCE 与重定向保护依据 [OAuth 安全最佳实践 RFC 9700](https://www.rfc-editor.org/rfc/rfc9700.html)。Books 不提供收集密码的 /login 或 /register API。

### 4.2 HTTP API 验证

API 使用 Authorization: Bearer access token。每次验证允许的签名算法、issuer、audience、exp、nbf、subject 和客户端身份；从固定 issuer 的元数据/JWKS 取密钥，不能接受令牌自带的任意密钥 URL。缓存公钥但未知 kid 只进行受限刷新；验证失败关闭访问。禁止把 bearer 放查询字符串、日志或审计。

ID token 的 audience 是登录客户端，不得拿来访问 Books API。access token 的受众必须是已配置的 Books 资源标识，目标部署 URI 尚未确定，本文不虚构域名。建议 access token 5 分钟；刷新及注销由提供商负责。**短令牌有效期不代替即时业务撤权**：每次请求都查当前 membership/grant/delegation。账号停用在 Books 中也有即时拒绝开关；仅在身份平台撤销而无本地同步时，已签发 JWT 的全局登录撤销时延可能到其到期，必须在认证验收记录此限制，不能宣称所有退出都瞬时失效。

账号停用不是首期对外管理端点。未来本地停用事务必须锁定该用户及其全部组织（固定排序），常规停用先要求交接；但不能为了保留管理员而阻止安全紧急停用。安全停用导致最后一位可登录管理员丧失资格时，建议将组织转为 locked_recovery，拒绝正常业务读写并走受审计恢复，不可静默留下可用的无人管理组织。该锁定/恢复状态是待审阅的异常技术策略，不增加已批准角色，也不授权自动恢复。外部身份平台单独停用/删号不受 Books 数据库约束控制：在没有验证停用事件同步与人工恢复流程前，只能保证数据库层 active 管理员不为零，不能保证该人仍能登录。身份停用同步、最后管理员恢复与恢复身份核验是上线前必过关口，不是已解决能力；不得自动把普通成员提升为管理员。

无需实现浏览器 cookie 会话。未来有 Web 再设计 BFF/cookie/CSRF；当前 bearer API 拒绝来源不明的认证方式混用。生产强制 TLS，允许的 CORS/Origin 明确列出，不使用通配凭证来源。

Keycloak 提供发现、令牌、公钥、userinfo、introspection 和撤销端点，参见[官方 OIDC 接入文档](https://www.keycloak.org/securing-apps/oidc-layers)。能提供这些端点不等于本项目的全部 MCP 接入已经验证。

## 5. 关系数据模型与约束

以下是设计规格，不是迁移脚本。所有 id 为服务器生成 UUID；所有被租户复合外键引用的实体均设 UNIQUE(org_id,id)，包括 invitations 和 delegations；时间为 timestamptz；可变聚合 version 为正 bigint，API 以十进制字符串输出，初值 "1"。名称 trim 后 1–200 Unicode 字符。禁止通过更新改变 org_id、company_id、user_id 等归属字段。物理删除组织/公司/账套/用户不在首期 API 内。

| 表 | 主要列 | 必要约束 |
| --- | --- | --- |
| users | id, status(active/disabled), display_name, created_at, version | 不存密码。关闭用户时所有入口实时拒绝。 |
| identities | id, user_id, issuer, subject, verified_email, email_verified_at | UNIQUE(issuer,subject)，user FK；邮箱非身份主键。 |
| organizations | id, name, state(active/locked_recovery，恢复策略待审阅), created_by, created_at, version | active组织至少一位active且本地未停用的admin；locked_recovery不开放正常业务；组织行兼作权限事务锁。 |
| memberships | id, org_id, user_id, role(admin/member), status(active/removed), generation, version, joined_at, removed_at | UNIQUE(org_id,user_id)，UNIQUE(org_id,id)；generation 正数，每次重新加入递增。 |
| companies | id, org_id, name, created_at, version | UNIQUE(org_id,id)，组织 FK。 |
| ledgers | id, org_id, company_id, name, created_at, version | UNIQUE(company_id)，UNIQUE(org_id,company_id)，UNIQUE(org_id,id)；(org_id,company_id) 复合 FK 到 companies。 |
| ledger_grants | org_id, membership_id, ledger_id, membership_generation, access(read/edit), granted_by, created_at | PK(membership_id,ledger_id)；分别用 (org_id,membership_id)、(org_id,ledger_id) 复合 FK；授权对应当前 active member/generation。 |
| invitations | id, org_id, target_email_key, inviter_membership_id, proposed_role, status, expires_at, accepted_by, accepted_at, created_at, version | 邀请人复合 FK 到同组织 membership；一组织一目标最多一条 status=pending；expires_at=created_at+7天（168小时）。 |
| invitation_grants | org_id, invitation_id, ledger_id, access | (org_id,invitation_id)、(org_id,ledger_id) 复合 FK；PK(invitation_id,ledger_id)；admin 邀请不带账套 grants。 |
| audit_events | id, org_id(nullable for user actions), actor_user_id, actor_client_id, delegation_id, action, target_type/id, safe_change, request_id, occurred_at | 追加写；运行账号不可更新/删除；与对应变更同事务提交。 |
| idempotency_records | actor_user_id, client_id, scope_key, operation, key, request_hash, outcome_resource_ids, status, created_at, expires_at | 作用域组合 UNIQUE；结果不保存 bearer/邀请秘密；过期后不能再承诺重放。 |
| delegations | id, user_id, client_id, org_id, resource_mode, scopes, status, expires_at, version | 一个用户/客户端/组织仅一条 active；角色改变不扩大它的权限；无共享用户。 |
| delegation_ledgers | delegation_id, org_id, ledger_id, access_cap | 同组织复合 FK；只定义允许的最大范围，不直接授予业务权限。 |

### 5.1 公司必须恰好一个账套

仅 ledger.company_id UNIQUE 只能保证“最多一个”。额外使 companies 的 (org_id,id) 反向引用 ledgers 的唯一 (org_id,company_id)，两侧外键均 DEFERRABLE INITIALLY DEFERRED。服务同事务插入公司和账套，提交时形成完整一对一，无法提交只有公司的孤立记录。两个方向禁止级联删除；将来如果允许公司删除，另定历史保护流程。

普通 CHECK 不适合跨行管理员计数或查别表；使用复合外键/唯一约束加事务锁，管理员最少一位另配延迟约束触发器作为最后防线。依据 [PostgreSQL 约束文档](https://www.postgresql.org/docs/18/ddl-constraints.html)。触发器也必须遵循组织先锁、再检查当前状态的规则，不能只对旧快照数人数。

### 5.2 邮箱和邀请目标

首期邀请目标限定为批准身份平台的已验证邮箱。建议定义固定 email_key：trim、域名规范化并转小写，本地部分按身份平台经过验证的唯一性策略规范化；禁止擅自删除加号/点。具体 provider 不同可能影响邮箱等价性，认证小验证必须锁定同一策略用于创建邀请和接受。若无法保证唯一匹配则拒绝接受并报告，不自动猜账号。邮箱变更不转移 membership；接受时重新读取受信任的当前验证邮箱。

### 5.3 有效授权与索引

只有 membership.active 且 role=member 且 grant.membership_generation=membership.generation 时，grant 生效。提升管理员时删当前 grants；降级/移除删旧 grants；重新加入 generation+1，并仅插入本次明确 grants。历史授权留在审计，不通过保留旧活跃行恢复。

索引至少包括 memberships(user_id,status,org_id)、各组织资源(org_id,created_at,id)、ledger_grants(org_id,ledger_id)、invitations(target_email_key,status,expires_at)、invitations(org_id,inviter_membership_id,status)、audit_events(org_id,occurred_at,id)。不使用基于 now() 的部分唯一索引；创建邀请时先锁组织并将同目标已到期 pending 标为 expired，再利用 status=pending 部分唯一索引。

## 6. 事务、并发与撤权的明确次序

首期以正确性优先，PostgreSQL READ COMMITTED + 组织级行锁：所有组织内写命令先 SELECT organization FOR UPDATE，再读最新操作者成员身份、目标及授权；单组织变更串行化。所有组织服务先验证 organization.state=active；locked_recovery统一拒绝正常业务并给ORG_RECOVERY_REQUIRED，仅保留经独立审阅的恢复通道。安全相关读使用组织 FOR SHARE，在事务内授权并完整物化有限响应后结束事务。多组织列表只返回当前用户可见元数据，不批量写；如需跨组织锁按 UUID 排序。该保守策略限制同组织并发吞吐，后续依据实测再拆粒度，不能未经证明优化。锁行为依据[官方锁文档](https://www.postgresql.org/docs/18/explicit-locking.html)。

锁顺序固定：当前 user 状态锁（FOR SHARE；停用操作需要互斥）→ organization → delegation → membership/invitation/ledger → 幂等记录；同类多行按 id 排序。委托撤销先锁其 organization 再锁 delegation；MCP 请求也在组织锁后检查并锁委托，避免“委托锁等待组织锁”与反向顺序互锁。所有业务写入口包括 MCP 共享此顺序。写入、审计、成功幂等结果一并提交；崩溃或审计失败一起回滚。身份外部网络调用在事务外完成，不持锁等邮件/远程服务。

- 创建组织：串行化当前用户的同类幂等键；插入 organization 与首位 admin membership；组织无管理员不得提交。
- 创建公司：组织锁内检查当前 admin；公司和唯一账套同时写入。
- 移除/降级：admin 数量与变更在组织锁内；若结果为零，409 LAST_ADMIN；改变当前操作者自己也同样处理。该成员发出的 pending 邀请标 invalidated，移除目标的旧 pending 邀请也失效；grants、delegations 和审计一致更新。
- 权限降级：涉及该成员的有效委托保守全部 revoked，之后需本人重新明确建立委托；防止后续晋升静默恢复旧 AI 权限。
- 邀请接受：锁组织和邀请，检查状态、精确过期时刻、当前邮箱、邀请人仍是 active admin、账套范围、目标是否已有成员；原子创建/恢复成员、写新 grants、消费邀请和审计。
- 先接受后撤销：409 INVITE_ALREADY_ACCEPTED，不能代替移除成员；先撤销/过期/失效后接受：410 INVITE_UNAVAILABLE。
- 写命令先取得锁并成功提交，则它发生在撤权前；撤权先提交，之后取得锁的写命令必须按新状态拒绝。不能在事务外验证后无保护提交。
- 已物化或已经发送的读取无法远程收回。首期无导出/长任务/持续数据流；未来增加时另定分块再授权及取消机制，不承诺当前不具备的中断能力。

建议 lock_timeout=3秒、单命令 statement_timeout=10秒，超时返回可重试错误，不标记成功；死锁/序列化失败至多自动重试2次且保持同一幂等键，禁止重试不明结果外部副作用。

## 7. HTTP API 通用契约

路径前缀 /api/v1。请求 JSON；未知字段拒绝，重复 JSON key 拒绝，UUID 格式验证。请求体上限 256 KiB，批量 grants 上限100（均为提案）；无浮动权限字段或任意 JSON 扩展字段。未提供字段表示不改；显式 null 只允许规范声明可空字段；[] 表示明确清空列表。首期 name 不可 null，PATCH 仅 name。

成功单对象：{"data":{...},"request_id":"..."}；列表：{"data":[...],"page":{"next_cursor":null},"request_id":"..."}。无总数默认值；空列表不是错误。版本作为字符串如 "3"；返回强 ETag 如 "v3"。PATCH、PUT 和有状态动作要求 If-Match；缺失428 PRECONDITION_REQUIRED，不匹配412 VERSION_CONFLICT。JSON 字段 expected_version 不再重复；MCP 可使用 expected_version 映射同一前置条件。

时间点 ISO 8601 UTC RFC3339，输出带 Z；七天按168小时，now>=expires_at 即失效。首期不出现会计日期/金额字段；未来金额需十进制字符串及币种/精度契约另审，不预先把金额当浮点数。

错误统一 application/problem+json：type（固定文档类型标识）、title、status、code、detail（安全可读）、instance/request_id，及可选 field_errors。不回传 SQL、堆栈、token、其他组织名称。调用方按 code 而非 detail 文案判断。

| HTTP | code 示例 | 语义 |
| --- | --- | --- |
| 400 | INVALID_ARGUMENT / INVALID_CURSOR | 输入格式或游标不匹配 |
| 401 | UNAUTHENTICATED | 凭证缺失、过期或验证失败；给适当 WWW-Authenticate |
| 403 | EMAIL_NOT_VERIFIED / FORBIDDEN / INSUFFICIENT_SCOPE / ORG_RECOVERY_REQUIRED | 已认证但未满足可公开的资格或权限 |
| 404 | NOT_FOUND | 不存在或不可见组织/账套/邀请；不区分跨租户存在性 |
| 409 | LAST_ADMIN / ALREADY_MEMBER / INVITE_CONFLICT / IDEMPOTENCY_KEY_REUSED | 业务冲突，不自动覆盖 |
| 410 | INVITE_UNAVAILABLE | 已验证为目标本人/本组织管理员后才揭示过期/撤销/失效 |
| 412 / 428 | VERSION_CONFLICT / PRECONDITION_REQUIRED | 防止丢失更新 |
| 429 | RATE_LIMITED | Retry-After；建议按主体/客户端并按 IP 限制匿名认证入口 |
| 503 | TEMPORARILY_UNAVAILABLE | DB/验证依赖不可用或锁等待超时；不伪装为空列表 |

目标不可见先404；目标可见但该操作不允许才403。错误机器码只增加不改义；破坏性字段/语义变化走 /v2 和新 MCP 工具版本，不在 v1 中静默改变。新增可选响应字段允许旧客户端忽略；枚举扩展需明确旧客户端处理方式。

### 7.1 幂等

所有业务写命令要求 Idempotency-Key（UUID），包括接受/撤销/离开/更改权限。作用域为 user_id + client_id + org或global + operation + key；规范化请求体、路径目标与 If-Match 共同计算指纹。相同键不同指纹409；相同键相同请求重放同一逻辑结果；并发同键等待首次事务后读取结果，超时503让调用方用原键重试。

建议保留7天，响应返回 Idempotency-Expires-At。超过保留期不得假设仍然去重，创建型客户端应先查询结果；重试未知结果保留原键。仅缓存已提交成功结果；验证失败不消耗键，事务失败不落成功状态。重放前先验证当前身份、委托和目标访问权，不能从缓存绕过撤权。若原请求是本人退出/自降级，允许相同主体仅重放安全的操作收据（id、完成状态、时间），不返回旧组织内容；其他不可见情况404。资源随后改变时重放的是原操作收据/资源ID，不冒充当前详情，客户端另发GET。

### 7.2 分页与搜索

列表 limit 默认50、1–100；固定 created_at ASC,id ASC（审计按 occurred_at,id）。next_cursor 不透明、签名且绑定主体、组织、筛选、排序、最后键及首屏截止时间；有效期15分钟。客户端不能拿 A 组织游标查询 B。第一页取截止时间，后页排除其后新建记录；按 keyset 翻页，当前授权每页重新校验。删除/撤权/编辑导致的结果变化不保证跨页快照完整，不宣称永不重漏。

首期仅 name_prefix（大小写不敏感前缀）用于组织/公司/账套；role/status 用于管理员成员列表；邀请 status 过滤。所有筛选组合为 AND，未知筛选/排序返回400。不接受任意 SQL 排序字段；先授权过滤再分页。成员本人只可看自己身份，完整成员/邀请清单限 admin。

## 8. 端点与最小请求/响应

表中 org 是路径 org_id，member 是 membership_id。所有有目标路径均先检验归属。返回实体统一包含 id、相关 org_id、version、created_at；用户列表不公开身份平台 subject。管理员显示成员邮箱需最小化，审计不能携带认证秘密。

| 方法与路径（/api/v1 后） | 请求核心字段 | 权限及成功结果 |
| --- | --- | --- |
| GET /me | 无 | 有效登录；本人 id、display_name、verified、status；不返回 tokens |
| GET /me/organizations | 分页 | 当前 active memberships 范围；组织摘要及本人 role |
| POST /organizations | name | verified user；201 organization + 首位 membership；无既有 org 要求 |
| GET /organizations/{org} | 无 | active member；组织摘要与本人 role |
| PATCH /organizations/{org} | name | admin；200 最新组织；If-Match |
| GET /organizations/{org}/companies | name_prefix、分页 | admin 全部；member 仅具有有效 grant 的账套所对应公司 |
| POST /organizations/{org}/companies | name, ledger_name | admin；201 company + 唯一 ledger；两名均必填 |
| GET /organizations/{org}/companies/{company} | 无 | admin 或该公司账套获授权 member |
| PATCH /organizations/{org}/companies/{company} | name | admin；200；公司身份资料不等于普通成员账务可编辑权限 |
| GET /organizations/{org}/ledgers | name_prefix、分页 | 按当前角色/grants过滤，含effective_access及not_configured |
| GET /organizations/{org}/ledgers/{ledger} | 无 | admin 或当前 grant；返回账套摘要和effective_access |
| PATCH /organizations/{org}/ledgers/{ledger} | name | admin；基础配置管理；不允许编辑记账设置 |
| GET /organizations/{org}/memberships | role,status、分页 | admin；列表 |
| GET /organizations/{org}/memberships/me | 无 | active member；本人角色及当前 grants |
| PUT /organizations/{org}/memberships/{member}/access | role, grants:[{ledger_id,access}] | admin；替换式完整权限集；角色admin时grants必须[]；member时grants必填，[]明确无账套 |
| POST /organizations/{org}/memberships/{member}/remove | {} | admin；安全操作收据；不能移除最后admin |
| POST /organizations/{org}/memberships/me/leave | {} | 本人；安全操作收据；最后admin409 |
| GET /organizations/{org}/invitations | status、分页 | admin；列表，不含秘密 |
| POST /organizations/{org}/invitations | email,role,grants | admin；201邀请摘要；目标已是active成员返回409；同内容有效pending返回200既有邀请、不延长有效期；不同内容409，需撤销再发 |
| POST /organizations/{org}/invitations/{invite}/revoke | {} | admin；200收据；已accepted409 |
| GET /me/invitations | 分页 | verified user；只匹配当前已验证邮箱的待接受邀请摘要，含org显示名及拟授范围 |
| POST /me/invitations/{invite}/accept | {} | verified匹配本人主动操作；200 membership/grants；重复已消费由本人接受返回安全结果，不升级 |
| GET /organizations/{org}/audit-events | 分页；固定允许过滤 | admin；只读审计 |
| GET /me/delegations | 分页 | 本人直接登录客户端；委托摘要 |
| POST /me/delegations | client_id,org_id,resource_mode,scopes,ledger_caps,expires_at | 本人直接登录客户端；201，范围不得超过本人现权；不是普通成员给别人授权 |
| POST /me/delegations/{id}/revoke | {} | 本人；If-Match；200收据 |

邀请收件箱条目必须返回 invitation.version 与 ETag 对应值；accept 的 If-Match 使用该邀请版本，不是 membership 版本。

没有独立 POST ledger，不能为公司创建第二账套；没有批量导入、删除组织、转移公司或任意 SQL 端点。每个变更均返回新版本或只含安全信息的操作收据。

邀请首期建议走**已认证邀请收件箱**：邀请 id 不是 bearer 凭证，知道 id 不够，必须匹配已验证身份并明确 POST accept。无需发出可直接授权的秘密链接；这取代早稿中“如采用秘密链接则存摘要”的可选机制，不改变已确认业务。通知邮件不列入首期，API 不声称已送达。未来增加邮件需 outbox、可重试通知和送达状态，不在业务事务里直发。接受不是自动后台动作，也不以读取GET代替。

示例（无真实数据）：创建公司请求 {"name":"示例公司","ledger_name":"示例账套"}；201返回 {"data":{"company":{"id":"<uuid>","org_id":"<uuid>","name":"示例公司","version":"1"},"ledger":{"id":"<uuid>","company_id":"<uuid>","accounting_setup_status":"not_configured","version":"1"}},"request_id":"<request-id>"}。尖括号只作字段示意，不是可发送的有效 UUID。

管理员交接由两条独立、可审计命令完成：先把另一有效成员设为 admin（grants=[]），确认成功，再本人 leave/降级。无需引入 owner 角色或特殊绕权交接端点。

## 9. MCP：范围受限的人类委托，而非超级账号

MCP 按选定协议版本2025-11-25设计，实施时核验目标客户端支持；此处不是宣称它始终是最新协议。参见 [MCP 授权规范](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization)。

1. /mcp 是受保护资源，发布 RFC9728 protected-resource metadata，并给401响应合法 WWW-Authenticate 的 resource_metadata 发现信息。授权服务器用 OIDC/OAuth 元数据发现。
2. 客户端走标准 Authorization Code + PKCE；首期仅批准的预注册客户端，不开放无限动态注册。客户端必须请求 Books MCP resource/audience，后台验证精准受众；参考 [RFC8707](https://www.rfc-editor.org/rfc/rfc8707.html) 和 [RFC9728](https://www.rfc-editor.org/rfc/rfc9728.html)。具体 provider 如何正确支持由兼容验证确认，不能只配静态 aud 就假定符合所有 resource 请求语义。
3. 每个请求识别真实 user_id 与受信任 client_id；MCP 参数不含 actor_user_id。客户端分类、scope与delegation判断属于公共Principal/应用服务授权，不按URL是否/mcp决定。即使MCP客户端取得有效API受众token并改调HTTP路径，也仍须同一委托交集校验；不能以换入口绕过。HTTP只接受API受众，MCP只接受MCP受众，错误受众在进入业务前拒绝；受众合法也不代表客户端属于直接人工客户端。不允许 client_credentials 代表人操作组织，不用统一管理员密钥，不允许 token passthrough 到其他受众服务。
4. 请求包含 org_id 时，查该 user/client/org 的当前 active delegation。有效权限 = token scopes ∩ 当前用户组织/账套权限 ∩ delegation 范围。缺一项拒绝。授权记录不发 token，也不是自制 OAuth server。
5. 委托由本人通过直接测试客户端明确建立，MCP 客户端不能调用建立/扩权委托端点；目标客户端须在批准名单，期限建议最多30天且要求明确 expires_at。续期是新的明确批准，不自动延长。测试客户端可以调用撤销；撤销后下一请求即失败，不等 token 到期。
6. 委托 scopes 推荐 books:read、books:write、books:org-admin。books:org-admin 仅管理员可授予、只限指定组织；member只能读/编辑指定ledger_caps，不能授予org-admin。账套cap是只读或可编辑，全部范围不是默认值。必须显式指定 resource_mode：selected_ledgers 或 organization_admin。前者只允许 books:read/books:write 且 ledger_caps 必须非空；后者必须显式 books:org-admin、ledger_caps=[]、当前用户为admin，表示整个组织的管理委托（含今后新建账套），不得描述为账套限定。org-admin与非空账套caps的组合400拒绝，不能用caps假装限制可邀请管理员/授权其他账套的权力。MCP 首期不暴露注册、创建委托、接受邀请或创建组织，避免身份/授权建立由代理悄悄完成；这些能力保留直接API测试客户端。
7. 角色降级、成员移除或相关授权减少时，保守撤销该用户该组织全部委托；重新加入/提升不恢复。账号disabled全部拒绝。本人可主动撤销，无需组织管理员配合。

委托操作范围矩阵：

| 操作 | selected_ledgers | organization_admin |
| --- | --- | --- |
| 组织摘要 | 仅该组织名称/本人身份最小信息 | 该组织摘要 |
| 公司/账套读取 | 只允许caps内且当前用户有权的账套及对应公司 | 本组织全部；仍需books:read |
| 账套业务写入（后续） | caps=edit且用户当前edit且books:write | 本组织；仍需books:write与业务校验 |
| 公司/账套创建或基础配置 | 拒绝，不属于账务edit | 明确org-admin；本组织 |
| 成员、邀请、授权、审计 | 拒绝，即使数据涉及已选账套 | 明确org-admin；本组织全部 |
| 委托建立/扩大 | 两种都拒绝经代理客户端调用 | 两种都拒绝经代理客户端调用 |

组织级管理能力不可能同时保证只触及少数账套，因此必须由本人明确接受全组织管理范围；这是技术提案，不是用户现在授予真实AI访问权。普通成员永远不能创建organization_admin委托。

在同一进程中，MCP adapter 将已验证 Principal、参数、幂等键及预期版本传到公共应用服务，不做环回HTTP，不传特殊高权token；HTTP和MCP共用同一后端API能力而不是复制规则。若未来拆进程，必须重新设计受众绑定的委托/令牌交换，不直接复用错误受众token。

建议工具名映射：books_list_organizations、books_get_organization、books_list_companies、books_create_company、books_list_ledgers、books_get_ledger、books_list_memberships、books_set_member_access、books_remove_member、books_list_invitations、books_create_invitation、books_revoke_invitation、books_list_audit_events。变更工具必有 idempotency_key 和 expected_version（创建无需后者）；不提供通用“任意请求”工具。组织列表仅返回有有效委托的组织，不暴露用户其余组织。

每工具给严格 input/output schema；成功结果structuredContent复用HTTP DTO；业务失败isError=true并给稳定code/request_id，不能假成功。无效/过期token为传输层401；scope不够按协议403 challenge；业务对象拒绝按结构化错误，不通过工具描述提升权限。annotations仅辅助客户端，不是权限控制。除当前有限查询，不做后台持续会话权限快照。

**兼容性验证必须证明**：元数据发现→指定resource→用户登录与PKCE→签发正确aud/client身份→工具成功→错误aud被拒绝→撤销委托立即失败→refresh不能扩大scope→未知客户端被拒绝。没有结果前不得宣布MCP可接入。

## 10. 安全、审计与运维边界

审计成功变更至少含人类操作者、客户端、委托（如有）、组织、动作、资源ID、字段白名单前后值、UTC时间、request_id。成员邮箱仅在业务需要的受限数据中，审计尽量记录内部ID。auth token、密码、验证码和完整邀请凭证不落日志。失败访问进安全日志而非成功业务审计；限制异常请求大小和频率。

租户隔离第一版由显式 org_id 查询条件、服务授权、复合外键及测试保障；数据库运行账号无DDL/超级权限，迁移账号独立。**不宣称已经启用RLS**；若以后增加RLS，需额外验证连接池上下文隔离与绕过权限，不能把尚未实现的防线当交付。runtime禁止更新/删除audit表；管理员也没有修改审计API。

运行环境建议开发/测试/生产分离；这里只提出，不创建环境。上线前必须指定备份保留、恢复时间目标、数据库与身份平台恢复演练、TLS/域名、密钥保管与轮换、告警和日志保留策略。密钥仅用批准的机密管理设施，不能进Git；本文无凭据模板。数据库迁移由单独发布步骤执行；备份不是应用回滚保证，破坏性变更不在首期。

## 11. 实施里程碑与可验收证据（等待编码批准）

| 阶段 | 内容 | 完成判据 |
| --- | --- | --- |
| M0 认证与契约验证 | 锁定依赖版本；Keycloak/目标客户端兼容小验证；OpenAPI与MCP schema；不接真实业务数据 | 上述完整认证路径及反例通过；失败则先修订设计，不继续假设 |
| M1 数据与授权底座 | 版本化迁移；一对一约束；service授权；组织锁；审计与幂等 | 空库迁移通过；直接SQL无法插跨组织关联/孤公司；并发最后管理员保护通过 |
| M2 组织与公司账套 | me、组织、公司/账套记录CRUD限定集；过滤分页 | 创建原子；org切换隔离；member仅获授权公司；无会计字段默认值 |
| M3 邀请成员授权 | 收件箱邀请、接受、撤销、移除、角色/grants替换 | 7天边界、身份不匹配、邀请人降权、重入、重复与竞争全通过 |
| M4 MCP委托接入 | 限定工具、scopes、委托创建/撤销、错误映射 | HTTP/MCP同矩阵；无高权凭证；token与业务撤权测试通过 |
| M5 集成验收与发布提案 | 安全测试、负载基线、依赖扫描、恢复演练计划、运行手册 | 验收报告列出通过/失败/未跑；用户另行审阅发布，不自动部署 |

最低自动化测试清单：
- A/B两组织交叉替换每条路径/引用/游标；404不泄漏，数据库复合FK拒绝跨组织grant。
- 同一用户A管理员B只读；只读不得写；edit不得管理成员、授权、组织/公司配置。
- 无现有组织用户可以创建；未验证用户不可；创建者和组织原子成功，故障注入无孤对象。
- 公司/账套任一插入故障回滚；直接提交公司无账套失败；第二账套失败。
- 两管理员同步退出/相互移除/降级，以真实独立DB连接和屏障制造竞争，提交后至少一名。
- 请求授权后暂停，另一事务撤权，再恢复；按锁获得/提交顺序断言，不用sleep碰运气。
- 邀请在到期前、恰好到期和之后；接受/撤销竞争；邀请人失权后再提升仍不能复活旧邀请。
- 已成员接受不升权；移除后旧邀请无效；新邀请重新加入只获得新grant；降级grants=[]则无账套。
- 同幂等键同体并发一次写入一次审计；不同体409；成功后连接断开重试；撤权后重放不泄漏；过期键边界。
- 两客户端同version修改仅一个成功；428/412含稳定code；审计失败业务回滚。
- 列表先授权后分页；同时间次级键；游标改过滤/组织/主体拒绝；撤权后下页拒绝。
- JWT错误iss/aud/算法/过期/未来nbf；ID token冒充；未知kid刷新限流；无邮箱自动账号合并。
- MCP委托跨org/ledger/scope失败；MCP客户端改调HTTP、取得API受众token、调用委托创建/扩权端点仍失败；普通成员永不获org-admin；org-admin+受限caps组合拒绝；selected_ledgers不能读完整成员/审计、邀请admin或为其他账套授权；organization_admin明确全组织范围且不能跨组织；client_id伪造失败；本人撤销/成员移除立即失效。
- 安全停用最后可用管理员不被人数检查阻止，组织进入待审阅的恢复锁定状态；无自动提升；身份平台停用事件丢失/延迟和平台不可用行为明确记录，恢复方案未批准则不得上线。
- 错误响应与日志不含认证秘密、SQL、跨租户名称；审计不可由runtime修改/删除。

提交实施验收时附：最终commit、依赖锁定记录、迁移结果、OpenAPI/schema验证、单元/真实DB并发/HTTP/MCP集成测试结果、未测试清单。当前这些测试**均未执行**；本文只完成设计，不能写成“已通过”。

## 12. 审阅重点与下一步

建议审阅重点为：推荐栈及身份服务运维代价；首期只建组织权限底座；已认证邀请收件箱暂不发通知邮件；保守组织级锁；HTTP字段和错误；MCP显式委托的用户边界。其余技术细节可由实现阶段在不改变契约/风险的前提下完善。

审阅后更新决策记录，再取得明确编码指令。会计基础设置、科目与过账继续作为后续独立设计，不阻塞本阶段组织权限文档完成，也不能被此文档默认为批准。未创建账号、凭据、邀请、部署或运行迁移；未修改任何实际系统权限。
