namespace BiliDownloader.Services.Download;

/// <summary>
/// 下载错误分类器：将异常映射为可持久化的错误类型和可重试标记。
/// <para>
/// 设计思考：
/// - 纯函数，无状态，无依赖 → 静态类即可，不需要 DI 注册。
/// - 分类策略保守：只有明确可重试的错误（网络/CDN）才标记 IsRetryable = true，
///   避免误导用户反复重试不可恢复的错误（如 ffmpeg 缺失、磁盘满）。
/// - 不引入异常层次结构重构：基于现有异常类型判断，务实够用。
/// - 不放在 Executor 中：G0 边界设计规定 Executor 只负责执行，
///   错误分类是 Coordinator 的编排职责。
/// </para>
/// </summary>
internal static class DownloadErrorClassifier
{
    /// <summary>
    /// 根据异常类型分类错误并判断可重试性。
    /// </summary>
    /// <param name="ex">下载过程中抛出的异常</param>
    /// <returns>错误类型字符串和可重试标记</returns>
    public static (string ErrorType, bool IsRetryable) Classify(Exception ex)
    {
        // CDN 协议异常（Range 响应不匹配等）：可重试（换 CDN 节点可能成功）
        if (ex is DownloadProtocolException)
            return ("cdn", true);

        // 网络请求异常（连接失败、DNS 解析等）：可重试
        if (ex is HttpRequestException)
            return ("network", true);

        // 任务超时（非用户取消的 TaskCanceledException）：可重试
        if (ex is TaskCanceledException)
            return ("network", true);

        // 权限/认证异常：不可重试（需要用户重新登录或修改权限）
        if (ex is UnauthorizedAccessException)
            return ("auth", false);

        // ffmpeg 相关异常：不可重试（需要安装/修复 ffmpeg）
        if (ex.Message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
            return ("ffmpeg", false);

        // IO 异常（非协议类）：磁盘/文件错误，不可重试
        if (ex is IOException)
            return ("disk", false);

        // 未分类异常：保守策略，标记为不可重试
        return ("unknown", false);
    }
}
