using System.Collections.Generic;
using System.IO;

namespace NzbDrone.Core.MediaFiles.Azw
{
    public class Azw3File : AzwFile
    {
        private readonly MobiHeader _mobiHeader;

        public Azw3File(Stream stream)
        {
            _mobiHeader = new MobiHeader(stream, ReadFirstSection(stream));
        }

        public string Title => _mobiHeader.Title;
        public List<string> Authors => _mobiHeader.ExtMeta.StringList(100);
        public string Author => _mobiHeader.ExtMeta.StringOrNull(100);
        public string Isbn => _mobiHeader.ExtMeta.StringOrNull(104);
        public string Asin => _mobiHeader.ExtMeta.StringOrNull(113);
        public string PublishDate => _mobiHeader.ExtMeta.StringOrNull(106);
        public string Publisher => _mobiHeader.ExtMeta.StringOrNull(101);
        public string Imprint => _mobiHeader.ExtMeta.StringOrNull(102);
        public string Description => _mobiHeader.ExtMeta.StringOrNull(103);
        public string Source => _mobiHeader.ExtMeta.StringOrNull(112);
        public string Language => _mobiHeader.ExtMeta.StringOrNull(524);
        public uint Version => _mobiHeader.Version;
        public uint MobiType => _mobiHeader.MobiType;
    }
}
