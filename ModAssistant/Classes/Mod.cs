using System.Collections.Generic;
using ModAssistant.Pages;

namespace ModAssistant
{
    public class Mod
    {
        public string id;
        public string name;
        public Author author;
        public string overview;
        public string category;
        public string respository;
        public string createdDate;
        public string updatedDate;
        public string latestVersion;
        public FileVersion[] versions;
        public List<Mod> Dependents = new List<Mod>();
        public List<Mod> Dependencies = new List<Mod>();
        public Mods.ModListItem ListItem;

        public class Author
        {
            public string id;
            public string username;
        }

        public class FileVersion
        {
            public string version;
            public int alpha;
            public string[] dependencies;
            public string[] conflicts;
            public string status;
            public string uploadDate;
            public string url;
            public long fileSize;
            public string changelog;
            public string[] gameVersions;
            public string md5;
            public string sha256;
            public string[] files;
        }
    }

    public class InstalledMod
    {
        public Mod Mod { get; set; }
        public Mod.FileVersion Version { get; set; }
    }
}
