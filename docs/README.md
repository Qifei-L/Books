# Books 文档目录

状态：先搭建骨架，再逐章确认；发布文档不代表批准实施。

## 建议阅读与完善顺序

1. [项目目标与范围](01-project-scope.md)
2. [组织 成员与权限](02-organizations-permissions.md)
3. [公司与账套](03-companies-ledgers.md)
4. [科目管理](04-chart-of-accounts.md)
5. [凭证与记账](05-journals-posting.md)
6. [余额 流水与报表](06-reports.md)
7. [接口约定](07-api-conventions.md)
8. [测试与验收](08-testing-acceptance.md)
9. [部署与运维](09-deployment-operations.md)
10. [决策与待办](10-decisions.md)

组织、成员与权限是第一优先内容。公司账套及科目章节在其边界明确后继续补充。

## 技术设计提案

- [多组织与权限技术设计](design/organizations-permissions.md)：数据关系、授权校验、邀请及交接事务、API/MCP 边界与验收映射。提案待审阅，不代表技术选型或实现获批。

- [首期多组织基础：详细技术设计与接口契约](design/phase-1-technical-contract.md)：承接多组织设计，按已确认 C# 路线提出具体版本、数据约束、HTTP/MCP 契约和实施任务；具体细节属于待审阅提案，未授权编码或部署。

## 既有草案

- [后端与科目管理早期需求草案](requirements.md)：保留作为讨论材料；其中建议、默认值与阶段安排未经批准，后续以逐章审阅结果及决策记录为准。

## 文档工作方式

先确认业务含义与范围，再补业务规则及验收，之后才进入技术设计。C# + ASP.NET Core + EF Core + PostgreSQL 路线已确认，首期不引入 ABP；会计引擎、具体接口实现、数据结构及部署细节仍待审阅，不开始代码实施。
