using System;
using System.Drawing;
using System.IO;

namespace RainWorldDesktopPet.RainWorld
{
    /// <summary>
    /// Loads Rain World's original player atlases directly from resources.assets.
    /// The provider never starts RainWorld.exe or loads a Unity/Rain World assembly.
    /// </summary>
    public sealed class EmbeddedUnityAtlasProvider
    {
        private static readonly string[] RequiredBaseElements =
        {
            "BodyA", "HipsA", "HeadA0", "FaceA0", "PlayerArm0", "LegsA0"
        };

        private readonly RainWorldInstallation installation;

        public EmbeddedUnityAtlasProvider(RainWorldInstallation installation)
        {
            this.installation = installation;
            Status = "Embedded Rain World atlas has not been inspected.";
        }

        public string Status { get; private set; }

        public RainWorldAtlasSet TryLoadPlayerAtlases()
        {
            try
            {
                return LoadPlayerAtlases();
            }
            catch (Exception exception)
            {
                Status = "Could not load embedded Rain World player atlas: " + exception.Message;
                return null;
            }
        }

        public RainWorldAtlasSet LoadPlayerAtlases()
        {
            if (installation == null) throw new InvalidOperationException("Rain World installation is unavailable.");
            if (!File.Exists(installation.ResourcesAssetsPath))
                throw new FileNotFoundException("Rain World resources.assets was not found.", installation.ResourcesAssetsPath);

            RainWorldAtlasSet set = new RainWorldAtlasSet();
            try
            {
                using (UnitySerializedFileReader reader = new UnitySerializedFileReader(installation.ResourcesAssetsPath))
                {
                    RainWorldAtlas baseAtlas = LoadAtlas(reader, "rainWorld");
                    set.Add(baseAtlas);
                    ValidateBaseElements(set);

                    bool mscLoaded = false;
                    string mscWarning = null;
                    try
                    {
                        UnityTextAssetInfo mscMetadata;
                        UnityTexture2DInfo mscTexture;
                        bool hasMscMetadata = reader.TryReadTextAsset("rainworldmsc", out mscMetadata);
                        bool hasMscTexture = reader.TryReadTexture2D("rainworldmsc", out mscTexture);
                        if (hasMscMetadata && hasMscTexture)
                        {
                            RainWorldAtlas mscAtlas = LoadAtlas(reader, mscMetadata, mscTexture);
                            set.Add(mscAtlas);
                            mscLoaded = true;
                        }
                        else if (hasMscMetadata != hasMscTexture)
                        {
                            mscWarning = "rainworldmsc has only one of its TextAsset/Texture2D objects.";
                        }
                    }
                    catch (Exception exception)
                    {
                        // The original base variants remain useful. Optional
                        // MSC-specific head/face variants will use base fallbacks.
                        mscWarning = exception.Message;
                    }

                    Status = "Loaded original embedded rainWorld atlas from " + installation.ResourcesAssetsPath +
                        " (Unity " + reader.UnityVersion + ", base " + baseAtlas.Image.Width + "x" +
                        baseAtlas.Image.Height + (mscLoaded ? ", MSC included" : ", MSC unavailable") + ").";
                    if (!string.IsNullOrEmpty(mscWarning)) Status += " MSC warning: " + mscWarning;
                    return set;
                }
            }
            catch
            {
                set.Dispose();
                throw;
            }
        }

        private RainWorldAtlas LoadAtlas(UnitySerializedFileReader reader, string objectName)
        {
            UnityTextAssetInfo metadata = reader.ReadTextAsset(objectName);
            UnityTexture2DInfo texture = reader.ReadTexture2D(objectName);
            return LoadAtlas(reader, metadata, texture);
        }

        private RainWorldAtlas LoadAtlas(UnitySerializedFileReader reader, UnityTextAssetInfo metadata, UnityTexture2DInfo texture)
        {
            if (!string.Equals(metadata.Name, texture.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded atlas TextAsset/Texture2D names do not match: " +
                    metadata.Name + " / " + texture.Name);

            byte[] encodedPixels = reader.ReadTextureData(texture);
            Bitmap bitmap = DxtDecoder.DecodeTexture(texture, encodedPixels);
            string sourceName = installation.ResourcesAssetsPath + "#" + texture.Name;
            return RainWorldAtlasLoader.LoadFromMemory(sourceName, bitmap, metadata.Text);
        }

        private static void ValidateBaseElements(RainWorldAtlasSet set)
        {
            for (int i = 0; i < RequiredBaseElements.Length; i++)
            {
                AtlasSprite sprite;
                if (!set.TryGet(RequiredBaseElements[i], out sprite))
                    throw new InvalidDataException("Embedded rainWorld atlas is missing player element " + RequiredBaseElements[i] + ".");
            }
        }
    }
}
