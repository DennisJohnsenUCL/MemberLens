using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

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
            //TODO: Implement
            return new MemberAccessorCompletionSource();
        }
    }
}
