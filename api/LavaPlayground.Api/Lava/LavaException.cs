namespace LavaPlayground.Api.Lava;

/// <summary>
/// Thrown when a template fails to parse or render.
/// The message is intended to be shown directly to the template author.
/// </summary>
public class LavaException : Exception
{
    public LavaException(string message) : base(message) { }
}
