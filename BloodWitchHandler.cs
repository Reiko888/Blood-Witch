using Dusk;

namespace BloodWitch
{
    internal class BWContentHandler : ContentHandler<BWContentHandler>
    {
        internal BWAssets? bwAssets;

        public class BWAssets(DuskMod mod, string filePath) : AssetBundleLoader<BWAssets>(mod, filePath) { }


        public BWContentHandler(DuskMod mod) : base(mod)
        {
            RegisterContent("bloodwitch", out bwAssets);
        }
    }
}
