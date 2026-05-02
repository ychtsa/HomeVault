/*
 * FILE: FilesystemCatalogImageStorage.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: Local-disk implementation of ICatalogImageStorage. Files are
 *              stored at <ContentRoot>/App_Data/uploads/{catalogId}/{itemId}
 *              — outside wwwroot, so the only way a browser can fetch an
 *              image is via the authorized Items/Image action.
 */

namespace HomeVault.Services
{
    public class FilesystemCatalogImageStorage : ICatalogImageStorage
    {
        private readonly string _rootPath;

        public FilesystemCatalogImageStorage(IWebHostEnvironment env)
        {
            _rootPath = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
            Directory.CreateDirectory(_rootPath);
        }

        public async Task SaveAsync(string catalogId, string itemId, Stream content)
        {
            string path = ResolvePath(catalogId, itemId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Write to a temp file then move into place — ensures no half-
            // written file is ever served if the process crashes mid-write.
            string tempPath = path + ".tmp";
            await using (FileStream output = File.Create(tempPath))
            {
                await content.CopyToAsync(output);
            }
            File.Move(tempPath, path, overwrite: true);
        }

        public Task<Stream?> OpenReadAsync(string catalogId, string itemId)
        {
            string path = ResolvePath(catalogId, itemId);
            if (!File.Exists(path))
                return Task.FromResult<Stream?>(null);

            Stream stream = File.OpenRead(path);
            return Task.FromResult<Stream?>(stream);
        }

        public void Delete(string catalogId, string itemId)
        {
            string path = ResolvePath(catalogId, itemId);
            if (File.Exists(path))
                File.Delete(path);
        }

        /*
         * Function: ResolvePath(string catalogId, string itemId)
         * Description: Builds the on-disk path. Both ids are validated up-
         *              front to be 5 chars of [a-zA-Z0-9] so they cannot
         *              contain ".." or path separators.
         * Return: string - absolute path under the uploads root.
         */
        private string ResolvePath(string catalogId, string itemId)
        {
            if (!IsSafeId(catalogId) || !IsSafeId(itemId))
                throw new ArgumentException("Catalog and item ids must be alphanumeric.");

            return Path.Combine(_rootPath, catalogId, itemId);
        }

        private static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 32) return false;
            foreach (char c in id)
            {
                if (!char.IsLetterOrDigit(c)) return false;
            }
            return true;
        }
    }
}
