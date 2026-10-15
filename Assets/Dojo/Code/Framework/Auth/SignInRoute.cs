namespace Dojo.Framework.Auth
{
    /// <summary>
    /// Which page the sign-in browser opens on. Every route ends the same way - a WorkOS token -
    /// they differ only in where the player starts.
    /// </summary>
    public enum SignInRoute
    {
        /// <summary>WorkOS's own sign-in page, with nothing chosen for the player.</summary>
        Hosted,

        /// <summary>The sign-in page, with the email the player typed already filled in.</summary>
        Email,

        /// <summary>The sign-up page instead of sign-in, with the email filled in if there is one.</summary>
        SignUp,

        /// <summary>Straight to Google, skipping WorkOS's page.</summary>
        Google,

        /// <summary>Straight to Apple, skipping WorkOS's page.</summary>
        Apple,
    }
}
