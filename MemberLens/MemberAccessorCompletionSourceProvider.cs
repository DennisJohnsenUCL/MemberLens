using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace MemberLens
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [ContentType("csharp")]
    [Name("MemberAccessorCompletionSourceProvider")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal class MemberAccessorCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView)
        {
            return new MemberAccessorCompletionSource();
        }
    }
}
