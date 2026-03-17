using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Bibliophilarr
{
    public class BibliophilarrAuthor
    {
        public string AuthorName { get; set; }
        public int Id { get; set; }
        public string ForeignAuthorId { get; set; }
        public string Overview { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public bool Monitored { get; set; }
        public int QualityProfileId { get; set; }
        public string RootFolderPath { get; set; }
        public HashSet<int> Tags { get; set; }
    }

    public class BibliophilarrEdition
    {
        public string Title { get; set; }
        public string ForeignEditionId { get; set; }
        public string Overview { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public bool Monitored { get; set; }
    }

    public class BibliophilarrBook
    {
        public string Title { get; set; }
        public string ForeignBookId { get; set; }
        public string ForeignEditionId { get; set; }
        public string Overview { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public bool Monitored { get; set; }
        public BibliophilarrAuthor Author { get; set; }
        public int AuthorId { get; set; }
        public List<BibliophilarrEdition> Editions { get; set; }
    }

    public class BibliophilarrProfile
    {
        public string Name { get; set; }
        public int Id { get; set; }
    }

    public class BibliophilarrTag
    {
        public string Label { get; set; }
        public int Id { get; set; }
    }

    public class BibliophilarrRootFolder
    {
        public string Path { get; set; }
        public int Id { get; set; }
    }
}
