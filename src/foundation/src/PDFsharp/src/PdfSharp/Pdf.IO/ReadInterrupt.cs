// Original FreePDF implementation. See PROVENANCE.md.

namespace PdfSharp.Pdf.IO
{
    /// <summary>
    /// Lets the caller of <see cref="PdfReader"/> stop it while it opens a file, for example on cancellation or at a
    /// time limit. The reader calls <see cref="Check"/> for each token and in its passes over all objects; every
    /// 1,024th call asks the caller. A stop is thrown as a limit refusal (<see cref="Parser.LimitExceeded"/>), so no
    /// repair path takes it for damage.
    /// </summary>
    sealed class ReadInterrupt(Func<bool> shouldStop)
    {
        int _calls;

        /// <summary>The reader was stopped by this interrupt.</summary>
        internal bool Stopped { get; private set; }

        internal void Check()
        {
            if (++_calls < 1024)
                return;
            _calls = 0;
            if (Stopped || shouldStop())
            {
                Stopped = true;
                throw Parser.LimitExceeded("Reading the file was stopped by its caller.");
            }
        }
    }
}
