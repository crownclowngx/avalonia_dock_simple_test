using Xunit;

// 插件测试共享 Host 进程级注册表、生命周期诊断与可卸载 AssemblyLoadContext。
// G14 会把本程序集的覆盖率并入四个插件专项证据；并行集合之间的调度差异可能让
// 异步清理回调在采集停止前后跨界。串行化只约束测试执行器，不改变生产实现和断言。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
