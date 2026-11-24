using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class ImportResult
    {
        public ImportDecision<LocalBook> ImportDecision { get; private set; }
        public List<string> Errors { get; private set; }

        internal LibraryImportHoldReason? HoldReason { get; }

        internal bool PreserveExistingUnmappedReason { get; set; }

        internal int? PreservedExistingFileId { get; private set; }

        public ImportResultType Result
        {
            get
            {
                if (Errors.Any())
                {
                    if (ImportDecision.Approved)
                    {
                        return ImportResultType.Skipped;
                    }

                    return ImportResultType.Rejected;
                }

                return ImportResultType.Imported;
            }
        }

        internal static ImportResult ExistingFilePreserved(ImportDecision<LocalBook> decision, int fileId, string reason)
        {
            return new ImportResult(decision, reason)
            {
                PreservedExistingFileId = fileId > 0 ? fileId : null
            };
        }

        public ImportResult(ImportDecision<LocalBook> importDecision, params string[] errors)
            : this(importDecision, null, errors)
        {
        }

        internal ImportResult(ImportDecision<LocalBook> importDecision, LibraryImportHoldReason? holdReason, params string[] errors)
        {
            Ensure.That(importDecision, () => importDecision).IsNotNull();

            ImportDecision = importDecision;
            Errors = errors.ToList();
            HoldReason = holdReason;
        }
    }
}
