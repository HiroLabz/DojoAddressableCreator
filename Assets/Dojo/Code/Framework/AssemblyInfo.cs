using System.Runtime.CompilerServices;

// The edit-mode tests reach a few switches nothing else may touch - the popup layer's leave to be
// made outside Play, for one.
[assembly: InternalsVisibleTo("Dojo.Framework.Tests.EditMode")]
