namespace _7_Zip_Password_Manager.Services;

/// <summary>
/// 用户选择的 JSON 文件不是有效的密码文件时抛出。
/// </summary>
public class InvalidPasswordFileException : Exception
{
    public InvalidPasswordFileException(string message) : base(message) { }
    public InvalidPasswordFileException(string message, Exception inner) : base(message, inner) { }
}
