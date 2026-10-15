using System;

namespace Dojo.Framework.Content
{
    /// <summary>
    /// A content operation failed. Thrown at the boundary of the content assembly so nothing
    /// upstream has to catch a delivery-mechanism-specific exception type.
    /// </summary>
    /// <remarks>
    /// This exists so the startup error popup can say "could not reach the content server" without
    /// <c>Dojo.Game</c> referencing Addressables to find out what went wrong. The original failure
    /// is kept as the inner exception for the log.
    /// </remarks>
    public sealed class ContentException : Exception
    {
        public ContentException(string message) : base(message) { }

        public ContentException(string message, Exception inner) : base(message, inner) { }
    }
}
