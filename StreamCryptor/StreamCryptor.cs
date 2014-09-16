﻿using ProtoBuf;
using Sodium;
using StreamCryptor.Helper;
using StreamCryptor.Model;
using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace StreamCryptor
{
    /// <summary>
    /// Class to encrypt and decrypt files in a stream.
    /// Using: libsodium and protobuf-net.
    /// </summary>
    public static class StreamCryptor
    {
        private const int CURRENT_VERSION = 1;
        private const int MIN_VERSION = 1;
        private const int CHUNK_LENGTH = 1048576; //~1MB
        private const int CHUNK_COUNT_START = 0;
        private const int CHUNK_MIN_NUMBER = 0;
        private const int CHUNK_BASE_NONCE_LENGTH = 16;
        private const int CHUNK_CHECKSUM_LENGTH = 64;
        private const int HEADER_CHECKSUM_LENGTH = 64;
        private const int FOOTER_CHECKSUM_LENGTH = 64;
        private const int NONCE_LENGTH = 24;
        private const int MAX_FILENAME_LENGTH = 256;
        private const int ASYNC_KEY_LENGTH = 32;
        private const int MASKED_FILENAME_LENGTH = 11;
        private const string DEFAULT_FILE_EXTENSION = ".sccef"; //StreamCryptor Chunked Encrypted File
        private const string TEMP_FILE_EXTENSION = ".tmp";

        /// <summary>
        /// Generates an accumulated nonce.
        /// </summary>
        /// <param name="baseNonce">16 byte nonce.</param>
        /// <param name="chunkNumber">Number to accumulate.</param>
        /// <param name="isLastChunkInStream">Idicates if this chunk is the last in the stream.</param>
        /// <returns>An accumulated nonce.</returns>
        private static byte[] GetChunkNonce(byte[] baseNonce, int chunkNumber, bool isLastChunkInStream = false)
        {
            //validate the length of the baseNonce
            if (baseNonce == null || baseNonce.Length != CHUNK_BASE_NONCE_LENGTH)
            {
                throw new ArgumentOutOfRangeException("baseNonce", (baseNonce == null) ? 0 : baseNonce.Length,
                  string.Format("baseNonce must be {0} bytes in length.", CHUNK_BASE_NONCE_LENGTH));
            }
            //validate the chunkNumber
            if (chunkNumber < 0)
            {
                throw new ArgumentOutOfRangeException("chunkNumber", string.Format("chunkNumber must be {0} or positive.", CHUNK_MIN_NUMBER));
            }
            //convert the integer to byte[8] array
            byte[] chunkNumberAsByte = Utils.IntegerToLittleEndian(chunkNumber);
            //merge the base nonce with the chunk number
            byte[] concatNonce = ArrayHelpers.ConcatArrays(baseNonce, chunkNumberAsByte);
            //set the last part to 128
            if (isLastChunkInStream)
                concatNonce[23] |= 0x80;

            return concatNonce;
        }

        #region Synchronous Implementation
        /// <summary>
        /// (Self)Encrypts a file with libsodium and protobuf-net.
        /// </summary>
        /// <param name="senderKeyPair">The senders keypair.</param>
        /// <param name="inputFile">The input file.</param>
        /// <param name="fileExtension">Set a custom file extenstion: .whatever</param>
        /// <param name="maskFileName">Replaces the filename with some random name.</param>
        /// <returns>The name of the encrypted file.</returns>
        /// <remarks>The outputFolder is equal to the inputFolder.</remarks>
        public static string EncryptFileWithStream(KeyPair senderKeyPair, string inputFile, string outputFolder = null, string fileExtension = DEFAULT_FILE_EXTENSION, bool maskFileName = false)
        {
            //validate the senderPrivateKey
            if (senderKeyPair == null || senderKeyPair.PrivateKey.Length != ASYNC_KEY_LENGTH || senderKeyPair.PublicKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("keypair", "invalid keypair");
            }
            //validate the inputFile
            if (inputFile == null || inputFile.Length < 1)
            {
                throw new ArgumentOutOfRangeException("inputFile", (inputFile == null) ? 0 : inputFile.Length,
                  string.Format("inputFile must be greater {0} in length.", 0));
            }
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException("inputFile", "inputFile could not be found.");
            }
            //retrieve file info
            FileInfo inputFileInfo = new FileInfo(inputFile);
            if (inputFileInfo.Name.Length > MAX_FILENAME_LENGTH)
            {
                throw new ArgumentOutOfRangeException("inputFile", string.Format("inputFile name must be smaller {0} in length.", MAX_FILENAME_LENGTH));
            }
            //validate the file extension
            if (!fileExtension[0].Equals('.')) {
                throw new ArgumentOutOfRangeException("fileExtension", "fileExtension start with: .");
            }
            //Call the main method
            return EncryptFileWithStream(senderKeyPair.PrivateKey, senderKeyPair.PublicKey, senderKeyPair.PublicKey, inputFile, outputFolder, fileExtension, maskFileName);
        }

        /// <summary>
        /// Encrypts a file with libsodium and protobuf-net.
        /// </summary>
        /// <param name="senderKeyPair">The senders keypair.</param>
        /// <param name="recipientPublicKey">A 32 byte public key.</param>
        /// <param name="inputFile">The input file.</param>
        /// <param name="fileExtension">Set a custom file extenstion: .whatever</param>
        /// <param name="maskFileName">Replaces the filename with some random name.</param>
        /// <returns>The name of the encrypted file.</returns>
        /// <remarks>The outputFolder is equal to the inputFolder.</remarks>
        public static string EncryptFileWithStream(KeyPair senderKeyPair, byte[] recipientPublicKey, string inputFile, string outputFolder = null, string fileExtension = DEFAULT_FILE_EXTENSION, bool maskFileName = false)
        {
            //validate the senderPrivateKey
            if (senderKeyPair == null || senderKeyPair.PrivateKey.Length != ASYNC_KEY_LENGTH || senderKeyPair.PublicKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("keypair", "invalid keypair");
            }
            //validate the recipientPublicKey
            if (recipientPublicKey == null || recipientPublicKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("recipientPublicKey", "invalid recipientPublicKey");
            }
            //validate the inputFile
            if (inputFile == null || inputFile.Length < 1)
            {
                throw new ArgumentOutOfRangeException("inputFile", (inputFile == null) ? 0 : inputFile.Length,
                  string.Format("inputFile must be greater {0} in length.", 0));
            }
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException("inputFile", "inputFile could not be found.");
            }
            //retrieve file info
            FileInfo inputFileInfo = new FileInfo(inputFile);
            if (inputFileInfo.Name.Length > MAX_FILENAME_LENGTH)
            {
                throw new ArgumentOutOfRangeException("inputFile", string.Format("inputFile name must be smaller {0} in length.", MAX_FILENAME_LENGTH));
            }
            //validate the file extension
            if (!fileExtension[0].Equals('.'))
            {
                throw new ArgumentOutOfRangeException("fileExtension", "fileExtension start with: .");
            }
            //Call the main method
            return EncryptFileWithStream(senderKeyPair.PrivateKey, senderKeyPair.PublicKey, recipientPublicKey, inputFile, outputFolder, fileExtension, maskFileName);
        }

        /// <summary>
        /// Encrypts a file with libsodium and protobuf-net.
        /// </summary>
        /// <param name="senderPrivateKey">A 32 byte private key.</param>
        /// <param name="senderPublicKey">A 32 byte public key.</param>
        /// <param name="recipientPublicKey">A 32 byte public key.</param>
        /// <param name="inputFile">The input file.</param>
        /// <param name="outputFolder">There the encrypted file will be stored, if this is null the input directory is used.</param>
        /// <param name="fileExtension">Set a custom file extenstion: .whatever</param>
        /// <param name="maskFileName">Replaces the filename with some random name.</param>
        /// <returns>The name of the encrypted file.</returns>
        public static string EncryptFileWithStream(byte[] senderPrivateKey, byte[] senderPublicKey, byte[] recipientPublicKey, string inputFile, string outputFolder = null, string fileExtension = DEFAULT_FILE_EXTENSION, bool maskFileName = false)
        {
            string outputFullPath = String.Empty;
            string outputFile = String.Empty;
            //validate the senderPrivateKey
            if (senderPrivateKey == null || senderPrivateKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("senderPrivateKey", "invalid senderPrivateKey");
            }
            //validate the senderPublicKey
            if (senderPublicKey == null || senderPublicKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("senderPublicKey", "invalid senderPublicKey");
            }
            //validate the recipientPublicKey
            if (recipientPublicKey == null || recipientPublicKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("recipientPublicKey", "invalid recipientPublicKey");
            }
            //validate the inputFile
            if (inputFile == null || inputFile.Length < 1)
            {
                throw new ArgumentOutOfRangeException("inputFile", (inputFile == null) ? 0 : inputFile.Length,
                  string.Format("inputFile must be greater {0} in length.", 0));
            }
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException("inputFile", "inputFile could not be found.");
            }
            //retrieve file info
            FileInfo inputFileInfo = new FileInfo(inputFile);
            if (inputFileInfo.Name.Length > MAX_FILENAME_LENGTH)
            {
                throw new ArgumentOutOfRangeException("inputFile", string.Format("inputFile name must be smaller {0} in length.", MAX_FILENAME_LENGTH));
            }
            //validate the file extension
            if (!fileExtension[0].Equals('.'))
            {
                throw new ArgumentOutOfRangeException("fileExtension", "fileExtension start with: .");
            }
            //validate the outputFolder
            if (outputFolder == null)
            {
                //use the same directory as inputFile
                outputFolder = inputFileInfo.DirectoryName;
            }
            else
            {
                if (!Directory.Exists(outputFolder))
                {
                    throw new DirectoryNotFoundException("outputFolder could not be found.");
                }
            }
            //generate the name of the output file
            if (maskFileName)
            {
                //store the output file with a masked file name and the fileExtension
                outputFile = Helper.Utils.GetRandomString(MASKED_FILENAME_LENGTH) + fileExtension;
                outputFullPath = Path.Combine(outputFolder, outputFile);
            }
            else
            {
                //store the output file, just with the fileExtension
                outputFile = inputFileInfo.Name + fileExtension;
                outputFullPath = Path.Combine(outputFolder, outputFile);
            }
            //prepare our file header
            EncryptedFileHeader encryptedFileHeader = new EncryptedFileHeader();
            //go for the streams
            using (FileStream fileStreamEncrypted = File.OpenWrite(outputFullPath))
            {
                using (FileStream fileStreamUnencrypted = File.OpenRead(inputFile))
                {
                    //get some ephemeral key fot this file
                    byte[] ephemeralKey = SecretBox.GenerateKey();
                    //generate a nonce for the encrypted ephemeral key
                    byte[] ephemeralNonce = Sodium.SodiumCore.GetRandomBytes(NONCE_LENGTH);
                    encryptedFileHeader.EphemeralNonce = ephemeralNonce;
                    //encrypt the ephemeral key with our public box 
                    byte[] encryptedEphemeralKey = Sodium.PublicKeyBox.Create(ephemeralKey, ephemeralNonce, senderPrivateKey, recipientPublicKey);
                    long fileLength = fileStreamUnencrypted.Length;
                    //set some things to the file header
                    encryptedFileHeader.UnencryptedFileLength = fileLength;
                    //set the senders public key to the header, to guarantee the recipient can decrypt it
                    encryptedFileHeader.SenderPublicKey = senderPublicKey;
                    //currently unsued
                    encryptedFileHeader.Version = CURRENT_VERSION;
                    //a random base nonce (16 byte), which will be filled up to 24 byte in every chunk
                    encryptedFileHeader.BaseNonce = SodiumCore.GetRandomBytes(CHUNK_BASE_NONCE_LENGTH);
                    //encryptedEphemeral
                    encryptedFileHeader.Key = encryptedEphemeralKey;
                    //the checksum to validate our file header
                    encryptedFileHeader.HeaderChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedFileHeader.BaseNonce, Utils.IntegerToLittleEndian(encryptedFileHeader.Version), encryptedFileHeader.Key, BitConverter.GetBytes(fileLength)), ephemeralKey, HEADER_CHECKSUM_LENGTH);
                    //encrypt the file name in the header
                    byte[] fileNameNonce = Sodium.SodiumCore.GetRandomBytes(NONCE_LENGTH);
                    encryptedFileHeader.FilenameNonce = fileNameNonce;
                    //get the filename to 256 bytes
                    byte[] paddedFileName = Helper.Utils.StringToPaddedByteArray(inputFileInfo.Name, MAX_FILENAME_LENGTH);
                    encryptedFileHeader.Filename = SecretBox.Create(paddedFileName, fileNameNonce, ephemeralKey);
                    //write the file header
                    Serializer.SerializeWithLengthPrefix(fileStreamEncrypted, encryptedFileHeader, PrefixStyle.Base128, 1);
                    //we start at chunk number 0
                    int chunkNumber = CHUNK_COUNT_START;
                    //Prepare the EncryptedFileFooter
                    EncryptedFileFooter encryptedFileFooter = new EncryptedFileFooter();
                    encryptedFileFooter.FooterNonceCount = SodiumCore.GetRandomBytes(NONCE_LENGTH);
                    encryptedFileFooter.FooterNonceLength = SodiumCore.GetRandomBytes(NONCE_LENGTH);
                    long overallChunkLength = 0;
                    //start reading the unencrypted file in chunks of the given length: CHUNK_LENGTH
                    byte[] unencryptedChunk = new byte[CHUNK_LENGTH];
                    int bytesRead;
                    do
                    {
                        byte[] chunkNonce = new byte[NONCE_LENGTH];
                        bytesRead = fileStreamUnencrypted.Read(unencryptedChunk, 0, CHUNK_LENGTH);
                        //check if there is still some work
                        if (bytesRead != 0)
                        {
                            //prepare the EncryptedFileChunk
                            EncryptedFileChunk encryptedFileChunk = new EncryptedFileChunk();
                            byte[] readedBytes = new byte[bytesRead];
                            //cut unreaded bytes
                            Array.Copy(unencryptedChunk, readedBytes, bytesRead);
                            //check if the file is smaller or equal the CHUNK_LENGTH
                            if (fileLength <= CHUNK_LENGTH)
                            {
                                //so we have the one and only chunk
                                chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, true);
                                encryptedFileChunk.ChunkIsLast = true;
                            }
                            else
                            {
                                //let`s check if this chunk is smaller than the given CHUNK_LENGTH
                                if (bytesRead < CHUNK_LENGTH)
                                {
                                    //it`s the last chunk in the stream
                                    chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, true);
                                    encryptedFileChunk.ChunkIsLast = true;
                                }
                                else
                                {
                                    //it`s a full chunk
                                    chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, false);
                                    encryptedFileChunk.ChunkIsLast = false;
                                }
                            }
                            //we also set the current chunk number to validate the nonce later
                            encryptedFileChunk.ChunkNumber = chunkNumber;
                            //set the chunk nonce (it containes the chunkNumber too)
                            encryptedFileChunk.ChunkNonce = chunkNonce;
                            //sym encrypt the chunk 
                            byte[] encryptedChunk = SecretBox.Create(readedBytes, chunkNonce, ephemeralKey);
                            //set the encrypted chunk
                            encryptedFileChunk.Chunk = encryptedChunk;
                            //and also the length of it
                            encryptedFileChunk.ChunkLength = encryptedChunk.Length;
                            //increment the OverallChunkLength
                            overallChunkLength += encryptedChunk.Length;
                            //generate a 64 byte checksum for this chunk
                            encryptedFileChunk.ChunkChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedChunk, Utils.IntegerToLittleEndian(encryptedChunk.Length), chunkNonce), ephemeralKey, CHUNK_CHECKSUM_LENGTH);
                            //write encryptedFileChunk to the output stream
                            Serializer.SerializeWithLengthPrefix(fileStreamEncrypted, encryptedFileChunk, PrefixStyle.Base128, 2);
                            //increment for the next chunk
                            chunkNumber++;
                        }
                        else
                        {
                            //Encrypt the footer data
                            encryptedFileFooter.ChunkCount = BitConverter.GetBytes(chunkNumber);
                            encryptedFileFooter.OverallChunkLength = BitConverter.GetBytes(overallChunkLength);
                            encryptedFileFooter.ChunkCount = SecretBox.Create(encryptedFileFooter.ChunkCount, encryptedFileFooter.FooterNonceCount, ephemeralKey);
                            encryptedFileFooter.OverallChunkLength = SecretBox.Create(encryptedFileFooter.OverallChunkLength, encryptedFileFooter.FooterNonceLength, ephemeralKey);
                            encryptedFileFooter.FooterChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedFileFooter.ChunkCount, encryptedFileFooter.OverallChunkLength), ephemeralKey, FOOTER_CHECKSUM_LENGTH);
                            //put the footer to the stream
                            Serializer.SerializeWithLengthPrefix(fileStreamEncrypted, encryptedFileFooter, PrefixStyle.Base128, 3);
                        }
                    } while (bytesRead != 0);
                }
            }
            return outputFile;
        }

        /// <summary>
        /// Decrypts a file with libsodium and protobuf-net.
        /// </summary>
        /// <param name="keyPair">The KeyPair to decrypt the ephemeralKey.</param>
        /// <param name="inputFile">An encrypted file.</param>
        /// <param name="outputFolder">There the decrypted file will be stored.</param>
        /// <param name="overWrite">Overwrite the output file if it exist.</param>
        /// <returns>The fullpath to the decrypted file.</returns>
        public static string DecryptFileWithStream(KeyPair keyPair, string inputFile, string outputFolder, bool overWrite = false)
        {
            //validate the keyPair (we also could ignore the PublicKey, it`s never used ...)
            if (keyPair == null || keyPair.PrivateKey.Length != ASYNC_KEY_LENGTH || keyPair.PublicKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("keyPair", "invalid keypair");
            }
            //validate the inputFile
            if (inputFile == null || inputFile.Length < 1)
            {
                throw new ArgumentOutOfRangeException("inputFile", (inputFile == null) ? 0 : inputFile.Length,
                    string.Format("inputFile must be greater {0} in length.", 0));
            }
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException("inputFile", "inputFile could not be found.");
            }
            //validate the outputFolder
            if (outputFolder == null || !Directory.Exists(outputFolder))
            {
                throw new DirectoryNotFoundException("outputFolder must exist");
            }
            return DecryptFileWithStream(keyPair.PrivateKey, inputFile, outputFolder, overWrite);
        }

        /// <summary>
        /// Decrypts a file with libsodium and protobuf-net.
        /// </summary>
        /// <param name="recipientPrivateKey">A 32 byte private key.</param>
        /// <param name="inputFile">An encrypted file.</param>
        /// <param name="outputFolder">There the decrypted file will be stored.</param>
        /// <param name="overWrite">Overwrite the output file if it exist.</param>
        /// <returns>The fullpath to the decrypted file.</returns>
        public static string DecryptFileWithStream(byte[] recipientPrivateKey, string inputFile, string outputFolder, bool overWrite = false)
        {
            string outputFile = String.Empty;
            string outputFullPath = String.Empty;
            //used to check the file length of the unencrypted file, will be renamed to the outputFile (if the file is valid)
            string tmpFile = String.Empty;
            string tmpFullPath = String.Empty;
            try
            {
                //validate the recipientPrivateKey
                if (recipientPrivateKey == null || recipientPrivateKey.Length != ASYNC_KEY_LENGTH)
                {
                    throw new ArgumentOutOfRangeException("recipientPrivateKey", "invalid recipientPrivateKey");
                }
                //validate the inputFile
                if (inputFile == null || inputFile.Length < 1)
                {
                    throw new ArgumentOutOfRangeException("inputFile", (inputFile == null) ? 0 : inputFile.Length,
                        string.Format("inputFile must be greater {0} in length.", 0));
                }
                if (!File.Exists(inputFile))
                {
                    throw new FileNotFoundException("inputFile", "inputFile could not be found.");
                }
                //validate the outputFolder
                if (outputFolder == null || !Directory.Exists(outputFolder))
                {
                    throw new DirectoryNotFoundException("outputFolder must exist");
                }

                //get a tmp name
                tmpFile = Utils.GetRandomString(MASKED_FILENAME_LENGTH) + TEMP_FILE_EXTENSION;
                tmpFullPath = Path.Combine(outputFolder, tmpFile);
                using (FileStream fileStreamEncrypted = File.OpenRead(inputFile))
                {
                    //first read the file header
                    EncryptedFileHeader encryptedFileHeader = new EncryptedFileHeader();
                    encryptedFileHeader = Serializer.DeserializeWithLengthPrefix<EncryptedFileHeader>(fileStreamEncrypted, PrefixStyle.Base128, 1);
                    //decrypt the ephemeral key with our public box 
                    byte[] ephemeralKey = Sodium.PublicKeyBox.Open(encryptedFileHeader.Key, encryptedFileHeader.EphemeralNonce, recipientPrivateKey, encryptedFileHeader.SenderPublicKey);
                    byte[] headerChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedFileHeader.BaseNonce, Utils.IntegerToLittleEndian(encryptedFileHeader.Version), encryptedFileHeader.Key, BitConverter.GetBytes(encryptedFileHeader.UnencryptedFileLength)), ephemeralKey, HEADER_CHECKSUM_LENGTH);
                    //check file header
                    if ((encryptedFileHeader.Version >= MIN_VERSION) &&
                        (encryptedFileHeader.BaseNonce.Length == CHUNK_BASE_NONCE_LENGTH) &&
                        (encryptedFileHeader.HeaderChecksum.SequenceEqual(headerChecksum)))
                    {
                        long overallChunkLength = 0;
                        //restore the original file name
                        byte[] encryptedPaddedFileName = encryptedFileHeader.Filename = SecretBox.Open(encryptedFileHeader.Filename, encryptedFileHeader.FilenameNonce, ephemeralKey); ;
                        //remove the padding
                        outputFile = Helper.Utils.PaddedByteArrayToString(encryptedPaddedFileName);
                        outputFullPath = Path.Combine(outputFolder, outputFile);
                        //keep the position for the footer
                        long fileStreamEncryptedPosition = 0;
                        int chunkNumber = CHUNK_COUNT_START;
                        //write the file to the tmpFullPath
                        using (FileStream fileStreamUnencrypted = File.OpenWrite(tmpFullPath))
                        {
                            //start reading the chunks
                            EncryptedFileChunk encryptedFileChunk = new EncryptedFileChunk();
                            while ((encryptedFileChunk = Serializer.DeserializeWithLengthPrefix<EncryptedFileChunk>(fileStreamEncrypted, PrefixStyle.Base128, 2)) != null)
                            {
                                //indicates if ChunkIsLast was found, to prepend more than one last chnunks.
                                bool isLastChunkFound = false;
                                byte[] chunkNonce = new byte[NONCE_LENGTH];
                                //check if this is the last chunk
                                if (encryptedFileChunk.ChunkIsLast)
                                {
                                    if (!isLastChunkFound)
                                    {
                                        //last
                                        chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, true);
                                        isLastChunkFound = true;
                                    }
                                    else
                                    {
                                        throw new BadLastFileChunkException("there are more than one last chunk, file could be damaged or manipulated!");
                                    }
                                }
                                else
                                {
                                    //there will propably come more
                                    chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, false);
                                }
                                //generate chunk checksum
                                byte[] chunkChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedFileChunk.Chunk, Utils.IntegerToLittleEndian(encryptedFileChunk.Chunk.Length), chunkNonce), ephemeralKey, CHUNK_CHECKSUM_LENGTH);
                                //check the current chunk checksum
                                if (chunkChecksum.SequenceEqual(encryptedFileChunk.ChunkChecksum))
                                {
                                    byte[] decrypted = SecretBox.Open(encryptedFileChunk.Chunk, chunkNonce, ephemeralKey);
                                    fileStreamUnencrypted.Write(decrypted, 0, decrypted.Length);
                                }
                                else
                                {
                                    throw new BadFileChunkException("Wrong checksum, file could be damaged or manipulated!");
                                }
                                chunkNumber++;
                                overallChunkLength += encryptedFileChunk.ChunkLength;
                                fileStreamEncryptedPosition = fileStreamEncrypted.Position;
                            }
                        }
                        //set the last position
                        fileStreamEncrypted.Position = fileStreamEncryptedPosition;
                        //prepare the EncryptedFileFooter
                        EncryptedFileFooter encryptedFileFooter = new EncryptedFileFooter();
                        //get the file footer and validate him
                        encryptedFileFooter = Serializer.DeserializeWithLengthPrefix<EncryptedFileFooter>(fileStreamEncrypted, PrefixStyle.Base128, 3);
                        if (encryptedFileFooter == null)
                        {
                            throw new BadFileFooterException("Missing file footer: file could be damaged or manipulated!");
                        }
                        byte[] chunkCount = BitConverter.GetBytes(chunkNumber);
                        byte[] chunkOverallLength = BitConverter.GetBytes(overallChunkLength);
                        byte[] footerChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(SecretBox.Create(chunkCount, encryptedFileFooter.FooterNonceCount, ephemeralKey), SecretBox.Create(chunkOverallLength, encryptedFileFooter.FooterNonceLength, ephemeralKey)), ephemeralKey, FOOTER_CHECKSUM_LENGTH);
                        //check the file footer
                        if (!footerChecksum.SequenceEqual(encryptedFileFooter.FooterChecksum))
                        {
                            throw new BadFileFooterException("Malformed file footer: file could be damaged or manipulated!");
                        }
                    }
                    else
                    {
                        throw new BadFileHeaderException("Malformed file header: file could be damaged or manipulated!");
                    }
                    //check the produced output for the correct length
                    //NOTICE: we also could use a file checksum, but this would lower the speed
                    if (encryptedFileHeader.UnencryptedFileLength == new FileInfo(tmpFullPath).Length)
                    {
                        //check if the new output file already exists
                        if (File.Exists(outputFullPath))
                        {
                            if (!overWrite)
                            {
                                //we don`t overwrite the file
                                throw new IOException("Decrypted file aleary exits, won`t overwrite");
                            }
                            else
                            {
                                //just delete the output file, so we can write a new one
                                File.Delete(outputFullPath);
                            }
                        }
                        File.Move(tmpFullPath, outputFullPath);
                    }
                    else
                    {
                        //File is not valid (return null)
                        outputFile = null;
                        File.Delete(tmpFullPath);
                    }
                }
            }
            catch (AggregateException ex)
            {
                //delete the temp file
                File.Delete(tmpFullPath);
                //and throw the exception
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
            return outputFile;
        }
        #endregion

        #region Asynchronous Implementation
        /// <summary>
        /// Encrypts a file asynchron with libsodium and protobuf-net.
        /// </summary>
        /// <param name="senderPrivateKey">A 32 byte private key.</param>
        /// <param name="senderPublicKey">A 32 byte public key.</param>
        /// <param name="recipientPublicKey">A 32 byte public key.</param>
        /// <param name="inputFile">The input file.</param>
        /// <param name="encryptionProgress">StreamCryptorTaskAsyncProgress object.</param>
        /// <param name="outputFolder">There the encrypted file will be stored, if this is null the input directory is used.</param>
        /// <param name="fileExtension">Set a custom file extenstion: .whatever</param>
        /// <param name="maskFileName">Replaces the filename with some random name.</param>
        /// <returns>The name of the encrypted file.</returns>
        /// <remarks>This method needs a revision.</remarks>
        public static async Task<string> EncryptFileWithStreamAsync(byte[] senderPrivateKey, byte[] senderPublicKey, byte[] recipientPublicKey, string inputFile, IProgress<StreamCryptorTaskAsyncProgress> encryptionProgress = null, string outputFolder = null, string fileExtension = DEFAULT_FILE_EXTENSION, bool maskFileName = false)
        {
            string outputFullPath = String.Empty;
            string outputFile = String.Empty;
            //validate the senderPrivateKey
            if (senderPrivateKey == null || senderPrivateKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("senderPrivateKey", "invalid senderPrivateKey");
            }
            //validate the senderPublicKey
            if (senderPublicKey == null || senderPublicKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("senderPublicKey", "invalid senderPublicKey");
            }
            //validate the recipientPublicKey
            if (recipientPublicKey == null || recipientPublicKey.Length != ASYNC_KEY_LENGTH)
            {
                throw new ArgumentOutOfRangeException("recipientPublicKey", "invalid recipientPublicKey");
            }
            //validate the inputFile
            if (inputFile == null || inputFile.Length < 1)
            {
                throw new ArgumentOutOfRangeException("inputFile", (inputFile == null) ? 0 : inputFile.Length,
                  string.Format("inputFile must be greater {0} in length.", 0));
            }
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException("inputFile", "inputFile could not be found.");
            }
            //retrieve file info
            FileInfo inputFileInfo = new FileInfo(inputFile);
            if (inputFileInfo.Name.Length > MAX_FILENAME_LENGTH)
            {
                throw new ArgumentOutOfRangeException("inputFile", string.Format("inputFile name must be smaller {0} in length.", MAX_FILENAME_LENGTH));
            }
            //validate the file extension
            if (!fileExtension[0].Equals('.'))
            {
                throw new ArgumentOutOfRangeException("fileExtension", "fileExtension start with: .");
            }
            //validate the outputFolder
            if (outputFolder == null)
            {
                //use the same directory as inputFile
                outputFolder = inputFileInfo.DirectoryName;
            }
            else
            {
                if (!Directory.Exists(outputFolder))
                {
                    throw new DirectoryNotFoundException("outputFolder could not be found.");
                }
            }
            //generate the name of the output file
            if (maskFileName)
            {
                //store the output file with a masked file name and the fileExtension
                outputFile = Helper.Utils.GetRandomString(MASKED_FILENAME_LENGTH) + fileExtension;
                outputFullPath = Path.Combine(outputFolder, outputFile);
            }
            else
            {
                //store the output file, just with the fileExtension
                outputFile = inputFileInfo.Name + fileExtension;
                outputFullPath = Path.Combine(outputFolder, outputFile);
            }
            //prepare our file header
            EncryptedFileHeader encryptedFileHeader = new EncryptedFileHeader();
            //go for the streams
            using (FileStream fileStreamEncrypted = File.OpenWrite(outputFullPath))
            {
                using (FileStream fileStreamUnencrypted = File.OpenRead(inputFile))
                {
                    //get some ephemeral key fot this file
                    byte[] ephemeralKey = SecretBox.GenerateKey();
                    //generate a nonce for the encrypted ephemeral key
                    byte[] ephemeralNonce = Sodium.SodiumCore.GetRandomBytes(NONCE_LENGTH);
                    encryptedFileHeader.EphemeralNonce = ephemeralNonce;
                    //encrypt the ephemeral key with our public box 
                    byte[] encryptedEphemeralKey = Sodium.PublicKeyBox.Create(ephemeralKey, ephemeralNonce, senderPrivateKey, recipientPublicKey);
                    long fileLength = fileStreamUnencrypted.Length;
                    //set some things to the file header
                    encryptedFileHeader.UnencryptedFileLength = fileLength;
                    //set the senders public key to the header, to guarantee the recipient can decrypt it
                    encryptedFileHeader.SenderPublicKey = senderPublicKey;
                    //currently unsued
                    encryptedFileHeader.Version = CURRENT_VERSION;
                    //a random base nonce (16 byte), which will be filled up to 24 byte in every chunk
                    encryptedFileHeader.BaseNonce = SodiumCore.GetRandomBytes(CHUNK_BASE_NONCE_LENGTH);
                    //encryptedEphemeral
                    encryptedFileHeader.Key = encryptedEphemeralKey;
                    //the checksum to validate our file header
                    encryptedFileHeader.HeaderChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedFileHeader.BaseNonce, Utils.IntegerToLittleEndian(encryptedFileHeader.Version), encryptedFileHeader.Key, BitConverter.GetBytes(fileLength)), ephemeralKey, HEADER_CHECKSUM_LENGTH);
                    //encrypt the file name in the header
                    byte[] fileNameNonce = Sodium.SodiumCore.GetRandomBytes(NONCE_LENGTH);
                    encryptedFileHeader.FilenameNonce = fileNameNonce;
                    //get the filename to 256 bytes
                    byte[] paddedFileName = Helper.Utils.StringToPaddedByteArray(inputFileInfo.Name, MAX_FILENAME_LENGTH);
                    encryptedFileHeader.Filename = SecretBox.Create(paddedFileName, fileNameNonce, ephemeralKey);
                    //write the file header
                    Serializer.SerializeWithLengthPrefix(fileStreamEncrypted, encryptedFileHeader, PrefixStyle.Base128, 1);
                    //we start at chunk number 0
                    int chunkNumber = CHUNK_COUNT_START;
                    //Prepare the EncryptedFileFooter
                    EncryptedFileFooter encryptedFileFooter = new EncryptedFileFooter();
                    encryptedFileFooter.FooterNonceCount = SodiumCore.GetRandomBytes(NONCE_LENGTH);
                    encryptedFileFooter.FooterNonceLength = SodiumCore.GetRandomBytes(NONCE_LENGTH);
                    long overallChunkLength = 0;
                    //start reading the unencrypted file in chunks of the given length: CHUNK_LENGTH
                    byte[] unencryptedChunk = new byte[CHUNK_LENGTH];
                    int bytesRead;
                    long overallBytesRead = 0;
                    do
                    {
                        byte[] chunkNonce = new byte[NONCE_LENGTH];
                        bytesRead = await fileStreamUnencrypted.ReadAsync(unencryptedChunk, 0, CHUNK_LENGTH);
                        //check if there is still some work
                        if (bytesRead != 0)
                        {
                            //prepare the EncryptedFileChunk
                            EncryptedFileChunk encryptedFileChunk = new EncryptedFileChunk();
                            byte[] readedBytes = new byte[bytesRead];
                            //cut unreaded bytes
                            Array.Copy(unencryptedChunk, readedBytes, bytesRead);
                            //check if the file is smaller or equal the CHUNK_LENGTH
                            if (fileLength <= CHUNK_LENGTH)
                            {
                                //so we have the one and only chunk
                                chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, true);
                                encryptedFileChunk.ChunkIsLast = true;
                            }
                            else
                            {
                                //let`s check if this chunk is smaller than the given CHUNK_LENGTH
                                if (bytesRead < CHUNK_LENGTH)
                                {
                                    //it`s the last chunk in the stream
                                    chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, true);
                                    encryptedFileChunk.ChunkIsLast = true;
                                }
                                else
                                {
                                    //it`s a full chunk
                                    chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, false);
                                    encryptedFileChunk.ChunkIsLast = false;
                                }
                            }
                            //we also set the current chunk number to validate the nonce later
                            encryptedFileChunk.ChunkNumber = chunkNumber;
                            //set the chunk nonce (it containes the chunkNumber too)
                            encryptedFileChunk.ChunkNonce = chunkNonce;
                            //sym encrypt the chunk 
                            byte[] encryptedChunk = SecretBox.Create(readedBytes, chunkNonce, ephemeralKey);
                            //set the encrypted chunk
                            encryptedFileChunk.Chunk = encryptedChunk;
                            //and also the length of it
                            encryptedFileChunk.ChunkLength = encryptedChunk.Length;
                            //increment the OverallChunkLength
                            overallChunkLength += encryptedChunk.Length;
                            //generate a 64 byte checksum for this chunk
                            encryptedFileChunk.ChunkChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedChunk, Utils.IntegerToLittleEndian(encryptedChunk.Length), chunkNonce), ephemeralKey, CHUNK_CHECKSUM_LENGTH);
                            //write encryptedFileChunk to the output stream
                            Serializer.SerializeWithLengthPrefix(fileStreamEncrypted, encryptedFileChunk, PrefixStyle.Base128, 2);
                            //increment for the next chunk
                            chunkNumber++;
                            overallBytesRead += bytesRead;
                            //report status
                            if (encryptionProgress != null)
                            {
                                var args = new StreamCryptorTaskAsyncProgress();
                                args.ProgressPercentage = (int)(encryptedFileHeader.UnencryptedFileLength <= 0 ? 0 : (100 * overallBytesRead) / encryptedFileHeader.UnencryptedFileLength);
                                encryptionProgress.Report(args);
                            }
                        }
                        else
                        {
                            //Encrypt the footer data
                            encryptedFileFooter.ChunkCount = BitConverter.GetBytes(chunkNumber);
                            encryptedFileFooter.OverallChunkLength = BitConverter.GetBytes(overallChunkLength);
                            encryptedFileFooter.ChunkCount = SecretBox.Create(encryptedFileFooter.ChunkCount, encryptedFileFooter.FooterNonceCount, ephemeralKey);
                            encryptedFileFooter.OverallChunkLength = SecretBox.Create(encryptedFileFooter.OverallChunkLength, encryptedFileFooter.FooterNonceLength, ephemeralKey);
                            encryptedFileFooter.FooterChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedFileFooter.ChunkCount, encryptedFileFooter.OverallChunkLength), ephemeralKey, FOOTER_CHECKSUM_LENGTH);
                            //put the footer to the stream
                            Serializer.SerializeWithLengthPrefix(fileStreamEncrypted, encryptedFileFooter, PrefixStyle.Base128, 3);
                        }
                    } while (bytesRead != 0);
                }
            }
            return outputFile;
        }

        /// <summary>
        /// Decrypts a file asynchron with libsodium and protobuf-net.
        /// </summary>
        /// <param name="recipientPrivateKey">A 32 byte private key.</param>
        /// <param name="inputFile">An encrypted file.</param>
        /// <param name="outputFolder">There the decrypted file will be stored.</param>
        /// <param name="decryptionProgress">StreamCryptorTaskAsyncProgress object.</param>
        /// <param name="overWrite">Overwrite the output file if it exist.</param>
        /// <returns>The fullpath to the decrypted file.</returns>
        /// <remarks>This method needs a revision.</remarks>
        public static async Task<string> DecryptFileWithStreamAsync(byte[] recipientPrivateKey, string inputFile, string outputFolder, IProgress<StreamCryptorTaskAsyncProgress> decryptionProgress = null, bool overWrite = false)
        {
            string outputFile = String.Empty;
            string outputFullPath = String.Empty;
            //used to check the file length of the unencrypted file, will be renamed to the outputFile (if the file is valid)
            string tmpFile = String.Empty;
            string tmpFullPath = String.Empty;
            try
            {
                //validate the recipientPrivateKey
                if (recipientPrivateKey == null || recipientPrivateKey.Length != ASYNC_KEY_LENGTH)
                {
                    throw new ArgumentOutOfRangeException("recipientPrivateKey", "invalid recipientPrivateKey");
                }
                //validate the inputFile
                if (inputFile == null || inputFile.Length < 1)
                {
                    throw new ArgumentOutOfRangeException("inputFile", (inputFile == null) ? 0 : inputFile.Length,
                        string.Format("inputFile must be greater {0} in length.", 0));
                }
                if (!File.Exists(inputFile))
                {
                    throw new FileNotFoundException("inputFile", "inputFile could not be found.");
                }
                //validate the outputFolder
                if (outputFolder == null || !Directory.Exists(outputFolder))
                {
                    throw new DirectoryNotFoundException("outputFolder must exist");
                }

                //get a tmp name
                tmpFile = Utils.GetRandomString(MASKED_FILENAME_LENGTH) + TEMP_FILE_EXTENSION;
                tmpFullPath = Path.Combine(outputFolder, tmpFile);
                using (FileStream fileStreamEncrypted = File.OpenRead(inputFile))
                {
                    //first read the file header
                    EncryptedFileHeader encryptedFileHeader = new EncryptedFileHeader();
                    encryptedFileHeader = Serializer.DeserializeWithLengthPrefix<EncryptedFileHeader>(fileStreamEncrypted, PrefixStyle.Base128, 1);
                    //decrypt the ephemeral key with our public box 
                    byte[] ephemeralKey = Sodium.PublicKeyBox.Open(encryptedFileHeader.Key, encryptedFileHeader.EphemeralNonce, recipientPrivateKey, encryptedFileHeader.SenderPublicKey);
                    byte[] headerChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedFileHeader.BaseNonce, Utils.IntegerToLittleEndian(encryptedFileHeader.Version), encryptedFileHeader.Key, BitConverter.GetBytes(encryptedFileHeader.UnencryptedFileLength)), ephemeralKey, HEADER_CHECKSUM_LENGTH);
                    //check file header
                    if ((encryptedFileHeader.Version >= MIN_VERSION) &&
                        (encryptedFileHeader.BaseNonce.Length == CHUNK_BASE_NONCE_LENGTH) &&
                        (encryptedFileHeader.HeaderChecksum.SequenceEqual(headerChecksum)))
                    {
                        long overallChunkLength = 0;
                        long overallBytesRead = 0;
                        //restore the original file name
                        byte[] encryptedPaddedFileName = encryptedFileHeader.Filename = SecretBox.Open(encryptedFileHeader.Filename, encryptedFileHeader.FilenameNonce, ephemeralKey); ;
                        //remove the padding
                        outputFile = Helper.Utils.PaddedByteArrayToString(encryptedPaddedFileName);
                        outputFullPath = Path.Combine(outputFolder, outputFile);
                        //keep the position for the footer
                        long fileStreamEncryptedPosition = 0;
                        int chunkNumber = CHUNK_COUNT_START;
                        //write the file to the tmpFullPath
                        using (FileStream fileStreamUnencrypted = File.OpenWrite(tmpFullPath))
                        {
                            //start reading the chunks
                            EncryptedFileChunk encryptedFileChunk = new EncryptedFileChunk();
                            while ((encryptedFileChunk = Serializer.DeserializeWithLengthPrefix<EncryptedFileChunk>(fileStreamEncrypted, PrefixStyle.Base128, 2)) != null)
                            {
                                //indicates if ChunkIsLast was found, to prepend more than one last chnunks.
                                bool isLastChunkFound = false;
                                byte[] chunkNonce = new byte[NONCE_LENGTH];
                                //check if this is the last chunk
                                if (encryptedFileChunk.ChunkIsLast)
                                {
                                    if (!isLastChunkFound)
                                    {
                                        //last
                                        chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, true);
                                        isLastChunkFound = true;
                                    }
                                    else
                                    {
                                        throw new BadLastFileChunkException("there are more than one last chunk, file could be damaged or manipulated!");
                                    }
                                }
                                else
                                {
                                    //there will propably come more
                                    chunkNonce = GetChunkNonce(encryptedFileHeader.BaseNonce, chunkNumber, false);
                                }
                                //generate chunk checksum
                                byte[] chunkChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(encryptedFileChunk.Chunk, Utils.IntegerToLittleEndian(encryptedFileChunk.Chunk.Length), chunkNonce), ephemeralKey, CHUNK_CHECKSUM_LENGTH);
                                //check the current chunk checksum
                                if (chunkChecksum.SequenceEqual(encryptedFileChunk.ChunkChecksum))
                                {
                                    byte[] decrypted = SecretBox.Open(encryptedFileChunk.Chunk, chunkNonce, ephemeralKey);
                                    await fileStreamUnencrypted.WriteAsync(decrypted, 0, decrypted.Length);
                                    overallBytesRead += (long)decrypted.Length;
                                }
                                else
                                {
                                    throw new BadFileChunkException("Wrong checksum, file could be damaged or manipulated!");
                                }
                                chunkNumber++;
                                overallChunkLength += encryptedFileChunk.ChunkLength;
                                fileStreamEncryptedPosition = fileStreamEncrypted.Position;
                                //report status
                                if (decryptionProgress != null)
                                {
                                    var args = new StreamCryptorTaskAsyncProgress();
                                    args.ProgressPercentage = (int)(encryptedFileHeader.UnencryptedFileLength <= 0 ? 0 : (100 * overallBytesRead) / encryptedFileHeader.UnencryptedFileLength);
                                    decryptionProgress.Report(args);
                                }
                            }
                        }
                        //set the last position
                        fileStreamEncrypted.Position = fileStreamEncryptedPosition;
                        //prepare the EncryptedFileFooter
                        EncryptedFileFooter encryptedFileFooter = new EncryptedFileFooter();
                        //get the file footer and validate him
                        encryptedFileFooter = Serializer.DeserializeWithLengthPrefix<EncryptedFileFooter>(fileStreamEncrypted, PrefixStyle.Base128, 3);
                        if (encryptedFileFooter == null)
                        {
                            throw new BadFileFooterException("Missing file footer: file could be damaged or manipulated!");
                        }
                        byte[] chunkCount = BitConverter.GetBytes(chunkNumber);
                        byte[] chunkOverallLength = BitConverter.GetBytes(overallChunkLength);
                        byte[] footerChecksum = Sodium.GenericHash.Hash(ArrayHelpers.ConcatArrays(SecretBox.Create(chunkCount, encryptedFileFooter.FooterNonceCount, ephemeralKey), SecretBox.Create(chunkOverallLength, encryptedFileFooter.FooterNonceLength, ephemeralKey)), ephemeralKey, FOOTER_CHECKSUM_LENGTH);
                        //check the file footer
                        if (!footerChecksum.SequenceEqual(encryptedFileFooter.FooterChecksum))
                        {
                            throw new BadFileFooterException("Malformed file footer: file could be damaged or manipulated!");
                        }
                    }
                    else
                    {
                        throw new BadFileHeaderException("Malformed file header: file could be damaged or manipulated!");
                    }
                    //check the produced output for the correct length
                    //NOTICE: we also could use a file checksum, but this would lower the speed
                    if (encryptedFileHeader.UnencryptedFileLength == new FileInfo(tmpFullPath).Length)
                    {
                        //check if the new output file already exists
                        if (File.Exists(outputFullPath))
                        {
                            if (!overWrite)
                            {
                                //we don`t overwrite the file
                                throw new IOException("Decrypted file aleary exits, won`t overwrite");
                            }
                            else
                            {
                                //just delete the output file, so we can write a new one
                                File.Delete(outputFullPath);
                            }
                        }
                        File.Move(tmpFullPath, outputFullPath);
                    }
                    else
                    {
                        //File is not valid (return null)
                        outputFile = null;
                        File.Delete(tmpFullPath);
                    }
                }
            }
            catch (AggregateException ex)
            {
                //delete the temp file
                File.Delete(tmpFullPath);
                //and throw the exception
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
            return outputFile;
        }
        #endregion
    }
}
