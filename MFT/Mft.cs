﻿using System;
using System.Collections.Generic;
using System.Linq;
using MFT.Attributes;
using MFT.Other;
using NLog;

namespace MFT
{
    public class Mft
    {
        public Mft(byte[] rawbytes)
        {
            var logger = LogManager.GetCurrentClassLogger();

            FileRecords = new Dictionary<string, FileRecord>();
            FreeFileRecords = new Dictionary<string, FileRecord>();
            BadRecords = new List<FileRecord>();
            UninitializedRecords = new List<FileRecord>();

            const int blockSize = 1024;

            var fileBytes = new byte[1024];

            var index = 0;

            while (index < rawbytes.Length)
            {
                Buffer.BlockCopy(rawbytes, index, fileBytes, 0, blockSize);

                var f = new FileRecord(fileBytes, index);

                var key = $"{f.EntryNumber:X8}-{f.SequenceNumber:X8}";

                logger.Debug($"offset: 0x{f.Offset:X} flags: {f.EntryFlags} key: {key}");

                if ((f.EntryFlags & FileRecord.EntryFlag.FileRecordSegmentInUse) ==
                    FileRecord.EntryFlag.FileRecordSegmentInUse)
                {
                    FileRecords.Add(key, f);
                }
                else if (f.IsBad)
                {
                    BadRecords.Add(f);
                }
                else if (f.IsUninitialized)
                {
                    UninitializedRecords.Add(f);
                }
                else
                {
                    FreeFileRecords.Add(key, f);
                }

                index += blockSize;
            }

            var rootFolder = FileRecords.Single(t => t.Value.EntryNumber == 5).Value;
            var rootKey = $"{rootFolder.EntryNumber:X8}-{rootFolder.SequenceNumber:X8}";
            RootDirectory = new DirectoryItem("", rootKey, ".");
        }

        public DirectoryItem RootDirectory { get; }


        public Dictionary<string, FileRecord> FileRecords { get; }
        public Dictionary<string, FileRecord> FreeFileRecords { get; }

        public List<FileRecord> BadRecords { get; }
        public List<FileRecord> UninitializedRecords { get; }

        public void BuildFileSystem()
        {
            //read record
            //navigate up from each filename record to parent record, keeping keys in a stack (push pop)
            //once at root, pop each from stack and build into RootDirectory
            //starting at RootDirectory, if nodes do not exist, create and add going down each level as needed
            //if it does exist, use that and keep checking down the rest of the entries
            //this will build out all the directories

            foreach (var fileRecord in FileRecords)
            {
                //   logger.Info(fileRecord.Value);

                if (fileRecord.Value.MftRecordToBaseRecord.MftEntryNumber > 0 &&
                    fileRecord.Value.MftRecordToBaseRecord.MftSequenceNumber > 0)
                {
                    //will get this record via attributeList
                    continue;
                }

                var key = $"{fileRecord.Value.EntryNumber:X8}-{fileRecord.Value.SequenceNumber:X8}";

                if (RootDirectory.Key == key)
                {
                    continue;
                }

                foreach (var fileNameAttribute in fileRecord.Value.Attributes.Where(t =>
                    t.AttributeType == AttributeType.FileName))
                {
                    var fna = (FileName) fileNameAttribute;

                    if (fna.FileInfo.NameType == NameTypes.Dos)
                    {
                        continue;
                    }

                    var stack = GetDirectoryChain(fna);

                    //the stack will always end with the RootDirectory's key, so take it away
                    stack.Pop();

                    var startDirectory = RootDirectory;

                    var parentDir = ".";

                    while (stack.Count > 0)
                    {
                        var dirKey = stack.Pop();

                        if (startDirectory.SubItems.ContainsKey(dirKey))
                        {
                            startDirectory = startDirectory.SubItems[dirKey];

                            parentDir = $"{parentDir}\\{startDirectory.Name}";
                        }
                        else
                        {
                            var entry = FileRecords[dirKey];

                            var newDirName = GetFileNameFromFileRecord(entry);
                            var newDirKey = $"{entry.EntryNumber:X8}-{entry.SequenceNumber:X8}";

                            var newDir = new DirectoryItem(newDirName, newDirKey, parentDir);

                            startDirectory.SubItems.Add(newDirKey, newDir);

                            startDirectory = startDirectory.SubItems[newDirKey];
                        }
                    }

                    string itemKey;

                    var isDirectory = (fna.FileInfo.Flags & StandardInfo.Flag.IsDirectory) ==
                                      StandardInfo.Flag.IsDirectory;

                    if (isDirectory)
                    {
                        itemKey = $"{fileRecord.Value.EntryNumber:X8}-{fileRecord.Value.SequenceNumber:X8}";
                    }
                    else
                    {
                        itemKey =
                            $"{fileRecord.Value.EntryNumber:X8}-{fileRecord.Value.SequenceNumber:X8}-{fna.AttributeNumber:X8}";
                    }

                    var itemDir = new DirectoryItem(fna.FileInfo.FileName, itemKey, parentDir);

                    if (startDirectory.SubItems.ContainsKey(itemKey) == false)
                    {
                        startDirectory.SubItems.Add(itemKey, itemDir);
                    }
                }
            }
        }

        private string GetFileNameFromFileRecord(FileRecord fr)
        {
            var fi = fr.Attributes.SingleOrDefault(t =>
                t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.DosWindows);
            if (fi == null)
            {
                fi = fr.Attributes.SingleOrDefault(t =>
                    t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Windows);
            }

            if (fi == null)
            {
                fi = fr.Attributes.Single(t =>
                    t.AttributeType == AttributeType.FileName && ((FileName) t).FileInfo.NameType == NameTypes.Posix);
            }

            var fin = (FileName) fi;

            return fin.FileInfo.FileName;
        }

        private Stack<string> GetDirectoryChain(FileName fileName)
        {
            var stack = new Stack<string>();

            var parentKey =
                $"{fileName.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fileName.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";

            while (parentKey != RootDirectory.Key)
            {
                stack.Push(parentKey);

                var parentRecord = FileRecords[parentKey];

                var fileNameAttribute =
                    (FileName) parentRecord.Attributes.First(t => t.AttributeType == AttributeType.FileName);

                parentKey =
                    $"{fileNameAttribute.FileInfo.ParentMftRecord.MftEntryNumber:X8}-{fileNameAttribute.FileInfo.ParentMftRecord.MftSequenceNumber:X8}";
            }

            //add the root in case things change later and we need it
            stack.Push(RootDirectory.Key);

            return stack;
        }
    }
}