using System.IO;

namespace MyAvaloniaManagement.Models.FileSystem;

internal static class FileSystemPath
{
    // 辅助方法：判断是否为驱动器路径
    public static bool IsDrivePath(string path)
    {
        // 改进的驱动器路径判断逻辑，支持更多情况
        try
        {
            // 检查是否为驱动器根目录（如 C:\、D:\）
            if (path.Length >= 3 && path[1] == ':' && path[2] == '\\' && 
                (path.Length == 3 || (path.Length > 3 && path[3] == '\\')))
            {
                return true;
            }
            
            // 检查是否为 UNC 路径
            if (path.StartsWith("\\\\"))
            {
                return true;
            }
            
            // 对于其他情况，检查路径的根目录是否等于其自身
            DirectoryInfo dirInfo = new DirectoryInfo(path);
            return dirInfo.Root.FullName.Equals(path, System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // 如果出现异常，默认不视为驱动器路径
            return false;
        }
    }
}
