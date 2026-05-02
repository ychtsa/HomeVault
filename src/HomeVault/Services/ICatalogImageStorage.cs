/*
 * FILE: ICatalogImageStorage.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-05-02
 * DESCRIPTION: Abstraction over per-catalog image storage. Hides whether
 *              the bytes live on local disk, in object storage, or
 *              elsewhere — controllers depend only on this interface.
 */

namespace HomeVault.Services
{
    public interface ICatalogImageStorage
    {
        /*
         * Function: SaveAsync(string catalogId, string itemId, Stream content)
         * Description: Overwrites the image for the given (catalog, item)
         *              with the supplied stream. Caller has already
         *              verified the request is authorized.
         * Parameter: string catalogId - owner catalog (also the storage partition).
         * Parameter: string itemId - target item.
         * Parameter: Stream content - source bytes; consumed to EOF.
         * Return: Task - completes when the file has been persisted.
         */
        Task SaveAsync(string catalogId, string itemId, Stream content);

        /*
         * Function: OpenReadAsync(string catalogId, string itemId)
         * Description: Opens a read-only stream to the stored image.
         * Parameter: string catalogId - owner catalog.
         * Parameter: string itemId - item to read.
         * Return: Task<Stream?> - the stream, or null if no file exists.
         */
        Task<Stream?> OpenReadAsync(string catalogId, string itemId);

        /*
         * Function: Delete(string catalogId, string itemId)
         * Description: Removes the stored image; no-op if missing.
         * Parameter: string catalogId - owner catalog.
         * Parameter: string itemId - item to clear.
         * Return: void.
         */
        void Delete(string catalogId, string itemId);
    }
}
