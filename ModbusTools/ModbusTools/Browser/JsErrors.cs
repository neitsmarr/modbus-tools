namespace ModbusTools.Browser;

public static class JsErrors
{
    /// <summary>JS errors arrive as "message\nstack"; only the message is useful to show.</summary>
    public static string FirstLine(Exception exception) => exception.Message.Split('\n', 2)[0];
}
