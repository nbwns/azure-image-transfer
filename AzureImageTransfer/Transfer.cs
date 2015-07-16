using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Net;
using System.IO;

namespace AzureImageTransfer
{
    public class ImageTransfer
    {
        public string AccountName { get; set; }
        public string AccountKey { get; set; }
        public string ContainerName { get; set; }

        private CloudBlobContainer container;
        
        public ImageTransfer()
        { 
        
        }

        public ImageTransfer(string accountName, string accountKey, string containerName)
        {
            AccountName = accountName;
            AccountKey = accountKey;
            ContainerName = containerName;
        }
        
        public void Connect()
        {
            if (string.IsNullOrEmpty(AccountName)) {
                throw new ArgumentNullException("AccountName");
            }

            if (string.IsNullOrEmpty(AccountKey))
            {
                throw new ArgumentNullException("AccountKey");
            }

            if (string.IsNullOrEmpty(ContainerName))
            {
                throw new ArgumentNullException("ContainerName");
            }
                        
            // Retrieve storage account from connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                String.Format("DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1}", AccountName, AccountKey)
            );

            // Create the blob client.
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Retrieve reference to a previously created container.
            container = blobClient.GetContainerReference(ContainerName);
        }
        
        public string Transfer(string imageUrl, string blobName) 
        {
            string azureUri = null;
            if (string.IsNullOrEmpty(imageUrl))
            {
                throw new ArgumentNullException("imageUrl");
            }

            if (string.IsNullOrEmpty(blobName))
            {
                throw new ArgumentNullException("blobName");
            }
            
            //Retrieves stream from image url
            Stream imageStream = DownloadRemoteImageFile(imageUrl);

            if(imageStream != null)
            {
                // Retrieve reference to a blob named "myblob".
                CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobName);
                blockBlob.Properties.ContentType = "image/jpg";

                // Create or overwrite the "myblob" blob with contents from a local file.
                using (imageStream)//var fileStream = System.IO.File.OpenRead(@"C:\Users\Nicolas\SkyDrive\Food\trio houmous.jpg"))
                {
                    blockBlob.UploadFromStream(imageStream);
                }

                azureUri = container.Uri.AbsoluteUri + "/" + blobName;
            }

            return azureUri;
        }

        public Stream DownloadRemoteImageFile(string uri)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (Exception)
            {
                return null;
            }

            // Check that the remote file was found. The ContentType
            // check is performed since a request for a non-existent
            // image file might be redirected to a 404-page, which would
            // yield the StatusCode "OK", even though the image was not
            // found.
            if ((response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Moved ||
                response.StatusCode == HttpStatusCode.Redirect) &&
                response.ContentType.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {

                // if the remote file was found, download it
                return response.GetResponseStream();
            }
            else{
                return null;
            }
        }

    }
}
