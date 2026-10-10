# 采用 GPL-3.0：允许商用，但衍生作品必须同样开源

SharpDb 以 **GPL-3.0** 授权（`LICENSE` 全文 + `PackageLicenseExpression=GPL-3.0-only`）。选择的是 copyleft 而非宽松许可：任何人可以用它、可以商用、可以基于它做封装，但**一旦分发**——包括分发一个包了 SharpDb 的外壳——其衍生作品必须以同一许可开源。

反直觉之处在于"做封装也要开源"这条。它正是选 GPL 而不是 MIT/Apache 的原因：否则最省事的商业化路径就是把 SharpDb 包一层、闭源发布、不回头贡献，而这会掏空项目本身。GPL-3.0 保留了商业使用（收费、内部使用、SaaS 之外的分发都可以），只堵住"拿走并闭源分发"这一条。

**被否决的选项**：MIT / Apache-2.0（允许闭源分发，正是要避免的）；AGPL-3.0（在 GPL 之上对"通过网络提供服务"也加开源义务——SharpDb 是本地 stdio 进程、不起网络服务，这一条用不上，只会让许可文本与实际形态不符）。

Consequences：`LICENSE` 与包的 `PackageLicenseExpression` 必须一致，否则 `dotnet pack` 拒绝出包；README 需向使用者明确写出"可商用、分发必须开源"。`PackageId` 与 `ToolCommandName` 已是公开契约，许可证同样是公开契约——发布后更改会追溯性地改变既有使用者的义务，因此这一条尤其难改。
