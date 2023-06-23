namespace ModAssistant
{
    public class GithubRelease
    {
        public Asset[] assets;

        public class Asset
        {
            public string name;
            public string browser_download_url;
        }
    }
}
