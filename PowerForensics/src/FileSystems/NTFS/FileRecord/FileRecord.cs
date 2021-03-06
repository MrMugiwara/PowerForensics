﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PowerForensics.Ntfs
{
    public class FileRecord
    {
        #region Enums

        [Flags]
        public enum FILE_RECORD_FLAG
        {
            INUSE = 0x01,	// File record is in use
            DIR = 0x02	    // File record is a directory
        }

        #endregion Enums

        #region Properties

        public readonly string VolumePath;              // Path to Volume

        // File Record Header
        private readonly ushort OffsetOfUS;             // Offset of Update Sequence
        private readonly ushort SizeOfUS;		        // Size in words of Update Sequence Number & Array
        public readonly ulong LogFileSequenceNumber;    // $LogFile Sequence Number
        public readonly ushort SequenceNumber;          // Sequence number
        public readonly ushort Hardlinks;               // Hard link count
        private ushort OffsetOfAttribute;               // Offset of the first Attribute
        public readonly FILE_RECORD_FLAG Flags;                  // Flags
        public readonly bool Deleted;
        public readonly bool Directory;
        public readonly int RealSize;                  // Real size of the FILE record
        public readonly int AllocatedSize;             // Allocated size of the FILE record
        public readonly ulong ReferenceToBase;          // File reference to the base FILE record
        private readonly ushort NextAttrId;             // Next Attribute Id
        public readonly uint RecordNumber;              // Index number of this MFT Record

        // Attribute Array
        public readonly FileRecordAttribute[] Attribute;

        // $STANDARD_INFORMATION
        public readonly DateTime ModifiedTime;
        public readonly DateTime AccessedTime;
        public readonly DateTime ChangedTime;
        public readonly DateTime BornTime;
        public readonly StandardInformation.ATTR_STDINFO_PERMISSION Permission;

        // $FILE_NAME
        public readonly string FullName;
        public readonly string Name;
        public readonly ushort ParentSequenceNumber;
        public readonly ulong ParentRecordNumber;
        public readonly DateTime FNModifiedTime;
        public readonly DateTime FNAccessedTime;
        public readonly DateTime FNChangedTime;
        public readonly DateTime FNBornTime;

        #endregion Properties

        #region Constructors

        private FileRecord(byte[] recordBytes, string volume, int bytesPerFileRecord, bool fast)
        {
            if (Encoding.ASCII.GetString(recordBytes, 0x00, 0x04) == "FILE")
            {
                VolumePath = volume;
                OffsetOfUS = BitConverter.ToUInt16(recordBytes, 0x04);
                SizeOfUS = BitConverter.ToUInt16(recordBytes, 0x06);
                LogFileSequenceNumber = BitConverter.ToUInt64(recordBytes, 0x08);
                SequenceNumber = BitConverter.ToUInt16(recordBytes, 0x10);
                Hardlinks = BitConverter.ToUInt16(recordBytes, 0x12);
                OffsetOfAttribute = BitConverter.ToUInt16(recordBytes, 0x14);
                Flags = (FILE_RECORD_FLAG)BitConverter.ToUInt16(recordBytes, 0x16);
                Deleted = isDeleted(Flags);
                Directory = isDirectory(Flags);
                RealSize = BitConverter.ToInt32(recordBytes, 0x18);
                AllocatedSize = BitConverter.ToInt32(recordBytes, 0x1C);
                ReferenceToBase = BitConverter.ToUInt64(recordBytes, 0x20);
                NextAttrId = BitConverter.ToUInt16(recordBytes, 40);
                RecordNumber = BitConverter.ToUInt32(recordBytes, 44);
                Attribute = FileRecordAttribute.GetInstances(recordBytes, OffsetOfAttribute, bytesPerFileRecord, volume);

                #region AttributeProperties

                //bool hasAttributeList = false;
                //List<FileRecordAttribute> ListOfAttributes = new List<FileRecordAttribute>();

                foreach (FileRecordAttribute attr in Attribute)
                {
                    if (attr.Name == FileRecordAttribute.ATTR_TYPE.STANDARD_INFORMATION)
                    {
                        StandardInformation stdInfo = attr as StandardInformation;
                        ModifiedTime = stdInfo.ModifiedTime;
                        AccessedTime = stdInfo.AccessedTime;
                        ChangedTime = stdInfo.ChangedTime;
                        BornTime = stdInfo.BornTime;
                        Permission = stdInfo.Permission;
                    }
                    /*else if (attr.Name == FileRecordAttribute.ATTR_TYPE.ATTRIBUTE_LIST)
                    {
                        if (!(attr.NonResident))
                        {
                            AttributeList attributeList = attr as AttributeList;
                            hasAttributeList = true;

                            List<ulong> numList = new List<ulong>();

                            foreach (AttrRef attrRef in attributeList.AttributeReference)
                            {
                                if (!(numList.Contains(attrRef.RecordNumber)))
                                {
                                    numList.Add(attrRef.RecordNumber);
                                }
                            }

                            foreach (ulong i in numList)
                            {
                                if (recordArray[i] == null)
                                {
                                    recordArray[i] = new FileRecordTest(ref recordArray, bytes, bytesPerFileRecord * (int)i, bytesPerFileRecord, volume, false);
                                }

                                ListOfAttributes.AddRange(recordArray[i].Attribute);
                            }
                        }
                    }*/
                    else if (attr.Name == FileRecordAttribute.ATTR_TYPE.FILE_NAME)
                    {
                        FileName fN = attr as FileName;
                        if (!(fN.Namespace == 2))
                        {
                            Name = fN.Filename;
                            ParentSequenceNumber = fN.ParentSequenceNumber;
                            ParentRecordNumber = fN.ParentRecordNumber;
                            FNModifiedTime = fN.ModifiedTime;
                            FNAccessedTime = fN.AccessedTime;
                            FNChangedTime = fN.ChangedTime;
                            FNBornTime = fN.BornTime;
                        }
                    }
                }

                #endregion AttributeProperties

                #region FullName

                if (fast)
                {
                    FullName = Name;
                }
                else
                {
                    StringBuilder sb = new StringBuilder();
                    string volLetter = Helper.GetVolumeLetter(volume);

                    if (RecordNumber == 0)
                    {
                        sb.Append(volLetter);
                        sb.Append('\\');
                        sb.Append(Name);
                        FullName = sb.ToString();
                    }
                    else if (RecordNumber == 5)
                    {
                        FullName = volLetter;
                    }
                    else
                    {
                        FileRecord parent = new FileRecord(GetRecordBytes(volume, (int)ParentRecordNumber), volume, bytesPerFileRecord, false);
                        if (parent.SequenceNumber == this.ParentSequenceNumber)
                        {
                            sb.Append(parent.FullName);
                        }
                        else
                        {
                            sb.Append(@"$OrphanFiles");
                        }

                        if (Name != null)
                        {
                            sb.Append('\\');
                            FullName = sb.Append(Name).ToString();
                        }
                        else
                        {
                            FullName = sb.ToString();
                        }
                    }
                }

                #endregion FullName
            }
        }

        private FileRecord(ref FileRecord[] recordArray, byte[] bytes, int offset, int bytesPerFileRecord, string volume, bool fast)
        {
            if (Encoding.ASCII.GetString(bytes, 0x00 + offset, 0x04) == "FILE")
            {
                OffsetOfUS = BitConverter.ToUInt16(bytes, 0x04 + offset);
                SizeOfUS = BitConverter.ToUInt16(bytes, 0x06 + offset);
                LogFileSequenceNumber = BitConverter.ToUInt64(bytes, 0x08 + offset);
                SequenceNumber = BitConverter.ToUInt16(bytes, 0x10 + offset);
                Hardlinks = BitConverter.ToUInt16(bytes, 0x12 + offset);
                OffsetOfAttribute = BitConverter.ToUInt16(bytes, 0x14 + offset);
                Flags = (FILE_RECORD_FLAG)BitConverter.ToUInt16(bytes, 0x16 + offset);
                Deleted = isDeleted(Flags);
                Directory = isDirectory(Flags);
                RealSize = BitConverter.ToInt32(bytes, 0x18 + offset);
                AllocatedSize = BitConverter.ToInt32(bytes, 0x1C + offset);
                ReferenceToBase = BitConverter.ToUInt64(bytes, 0x20 + offset);
                NextAttrId = BitConverter.ToUInt16(bytes, 0x28 + offset);
                RecordNumber = BitConverter.ToUInt32(bytes, 0x2C + offset);
                Attribute = FileRecordAttribute.GetInstances(bytes, OffsetOfAttribute + offset, bytesPerFileRecord, volume);
                //FileRecordAttribute[] attributeArray = FileRecordAttribute.GetInstances(bytes, OffsetOfAttribute + offset, bytesPerFileRecord, volume);

                #region AttributeProperties

                //bool hasAttributeList = false;
                //List<FileRecordAttribute> ListOfAttributes = new List<FileRecordAttribute>();

                foreach (FileRecordAttribute attr in Attribute)
                {
                    if (attr.Name == FileRecordAttribute.ATTR_TYPE.STANDARD_INFORMATION)
                    {
                        StandardInformation stdInfo = attr as StandardInformation;
                        ModifiedTime = stdInfo.ModifiedTime;
                        AccessedTime = stdInfo.AccessedTime;
                        ChangedTime = stdInfo.ChangedTime;
                        BornTime = stdInfo.BornTime;
                        Permission = stdInfo.Permission;
                    }
                    /*else if (attr.Name == FileRecordAttribute.ATTR_TYPE.ATTRIBUTE_LIST)
                    {
                        if (!(attr.NonResident))
                        {
                            AttributeList attributeList = attr as AttributeList;
                            hasAttributeList = true;

                            List<ulong> numList = new List<ulong>();

                            foreach (AttrRef attrRef in attributeList.AttributeReference)
                            {
                                if (!(numList.Contains(attrRef.RecordNumber)))
                                {
                                    numList.Add(attrRef.RecordNumber);
                                }
                            }

                            foreach (ulong i in numList)
                            {
                                if (recordArray[i] == null)
                                {
                                    recordArray[i] = new FileRecordTest(ref recordArray, bytes, bytesPerFileRecord * (int)i, bytesPerFileRecord, volume, false);
                                }

                                ListOfAttributes.AddRange(recordArray[i].Attribute);
                            }
                        }
                    }*/
                    else if (attr.Name == FileRecordAttribute.ATTR_TYPE.FILE_NAME)
                    {
                        FileName fN = attr as FileName;
                        if (!(fN.Namespace == 2))
                        {
                            Name = fN.Filename;
                            ParentSequenceNumber = fN.ParentSequenceNumber;
                            ParentRecordNumber = fN.ParentRecordNumber;
                            FNModifiedTime = fN.ModifiedTime;
                            FNAccessedTime = fN.AccessedTime;
                            FNChangedTime = fN.ChangedTime;
                            FNBornTime = fN.BornTime;
                        }
                    }
                }

                #endregion AttributeProperties

                #region FullName

                if (fast)
                {
                    FullName = Name;
                }
                else
                {
                    StringBuilder sb = new StringBuilder();
                    string volLetter = Helper.GetVolumeLetter(volume);

                    if (RecordNumber == 0)
                    {
                        sb.Append(volLetter);
                        sb.Append('\\');
                        sb.Append(Name);
                        FullName = sb.ToString();
                    }
                    else if (RecordNumber == 5)
                    {
                        FullName = volLetter;
                    }
                    else
                    {
                        FileRecord parent = null;

                        if (recordArray[this.ParentRecordNumber] != null)
                        {
                            parent = recordArray[this.ParentRecordNumber];
                        }
                        else
                        {
                            parent = new FileRecord(ref recordArray, bytes, bytesPerFileRecord * (int)this.ParentRecordNumber, bytesPerFileRecord, volume, fast);
                            recordArray[this.ParentRecordNumber] = parent;
                        }

                        if (parent.SequenceNumber == this.ParentSequenceNumber)
                        {
                            sb.Append(parent.FullName);
                        }
                        else
                        {
                            sb.Append(@"$OrphanFiles");
                        }

                        if (Name != null)
                        {
                            sb.Append('\\');
                            FullName = sb.Append(Name).ToString();
                        }
                        else
                        {
                            FullName = sb.ToString();
                        }
                    }
                }

                #endregion FullName
            }
        }

        #endregion Constructors

        #region StaticMethods

        #region GetInstances

        public static FileRecord[] GetInstances(string volume)
        {
            return GetInstances(volume, false);
        }

        public static FileRecord[] GetInstancesByPath(string path)
        {
            FileRecord record = FileRecord.Get(path, true);
            byte[] mftBytes = record.GetContent();
            return GetInstances(mftBytes, Helper.GetVolumeFromPath(path), false);
        }

        internal static FileRecord[] GetInstances(string volume, bool fast)
        {
            FileRecord record = FileRecord.Get(volume, MftIndex.MFT_INDEX, true);
            byte[] mftBytes = record.GetContent();
            return GetInstances(mftBytes, volume, fast);
        }

        private static FileRecord[] GetInstances(byte[] bytes, string volume, bool fast)
        {
            VolumeBootRecord vbr = VolumeBootRecord.Get(volume);

            // Determine the size of an MFT File Record
            int bytesPerFileRecord = (int)vbr.BytesPerFileRecord;

            // Calulate the number of entries in the MFT
            int fileCount = bytes.Length / bytesPerFileRecord;

            // Instantiate an array of FileRecord objects
            FileRecord[] recordArray = new FileRecord[fileCount];

            // Apply fixup values across MFT Bytes
            ApplyFixup(ref bytes, bytesPerFileRecord);

            // Now we need to iterate through all possible index values
            for (int index = 0x00; index < fileCount; index++)
            {
                // Check if current record has been instantiated
                if (recordArray[index] == null)
                {
                    // Instantiate FileRecord object
                    recordArray[index] = new FileRecord(ref recordArray, bytes, index * bytesPerFileRecord, bytesPerFileRecord, volume, fast);
                }
            }

            return recordArray;
        }

        #endregion GetInstances

        #region Get

        public static FileRecord Get(string path)
        {
            return Get(path, false);
        }

        internal static FileRecord Get(string path, bool fast)
        {
            string volume = Helper.GetVolumeFromPath(path);
            IndexEntry entry = IndexEntry.Get(path);
            return Get(volume, (int)entry.RecordNumber, fast);
        }

        public static FileRecord Get(string volume, int index)
        {
            return Get(volume, index, false);
        }

        internal static FileRecord Get(string volume, int index, bool fast)
        {
            VolumeBootRecord vbr = VolumeBootRecord.Get(volume);
            byte[] bytes = GetRecordBytes(volume, index);
            return Get(bytes, volume, (int)vbr.BytesPerFileRecord, fast);
        }

        internal static FileRecord Get(byte[] bytes, string volume, int bytesPerFileRecord, bool fast)
        {
            return new FileRecord(bytes, volume, bytesPerFileRecord, fast);
        }

        #endregion Get

        #region GetRecordBytesMethod

        public static byte[] GetRecordBytes(string path)
        {
            string volume = Helper.GetVolumeFromPath(path);
            IndexEntry entry = IndexEntry.Get(path);
            return GetRecordBytesPrivate(volume, (int)entry.RecordNumber);
        }

        public static byte[] GetRecordBytes(string volume, int index)
        {
            return GetRecordBytesPrivate(volume, index);
        }

        private static byte[] GetRecordBytesPrivate(string volume, int index)
        {
            // Get filestream based on hVolume
            using (FileStream streamToRead = Helper.getFileStream(volume))
            {
                // Get Volume Boot Record
                VolumeBootRecord VBR = VolumeBootRecord.Get(streamToRead);

                // Determine start of MFT
                long mftStartOffset = VBR.MftStartIndex * VBR.BytesPerCluster;

                // Get FileRecord for $MFT
                FileRecord mftRecord = MasterFileTable.GetRecord(streamToRead, volume);

                // Get $MFT Data Attribute
                NonResident data = null;

                foreach (FileRecordAttribute attr in mftRecord.Attribute)
                {
                    if (attr.Name == FileRecordAttribute.ATTR_TYPE.DATA)
                    {
                        data = attr as NonResident;
                    }
                }

                // Iterate through fragments of the MFT
                foreach (DataRun dr in data.DataRun)
                {
                    long DataRunRecords = (dr.ClusterLength * VBR.BytesPerCluster) / VBR.BytesPerFileRecord;

                    // Check if index can be found in current DataRun
                    if (index < (int)DataRunRecords)
                    {
                        long recordOffset = (dr.StartCluster * VBR.BytesPerCluster) + (index * VBR.BytesPerFileRecord);
                        byte[] recordBytesRaw = Helper.readDrive(streamToRead, recordOffset, VBR.BytesPerFileRecord);

                        ApplyFixup(ref recordBytesRaw, (int)VBR.BytesPerFileRecord);

                        return recordBytesRaw;
                    }

                    // Decrement index for the number of FileRecords in the current DataRun
                    else
                    {
                        index -= ((int)dr.ClusterLength * VBR.BytesPerCluster) / (int)VBR.BytesPerFileRecord;
                    }
                }
                throw new Exception("Could not find the FileRecord requested...");
            }
        }

        #endregion GetRecordBytesMethod

        #region GetContentBytesMethods

        public static byte[] GetContentBytes(string path)
        {
            return GetContentBytes(path, "");
        }

        public static byte[] GetContentBytes(string path, string streamName)
        {
            FileRecord record = Get(path, true);
            return record.GetTestContent(streamName);
        }

        #endregion GetContentBytesMethods

        #endregion StaticMethods

        #region InstanceMethods

        // TODO: Add Encoding parameter
        // TODO: Add DataStream parameter
        #region GetContentMethods

        public byte[] GetContent()
        {
            foreach (FileRecordAttribute attr in this.Attribute)
            {
                if (attr.Name == FileRecordAttribute.ATTR_TYPE.DATA)
                {
                    if (attr.NameString == "")
                    {
                        if (attr.NonResident)
                        {
                            return (attr as NonResident).GetBytes(this.VolumePath);
                        }
                        else
                        {
                            return (attr as Data).RawData;
                        }
                    }
                }
                else if (attr.Name == FileRecordAttribute.ATTR_TYPE.ATTRIBUTE_LIST)
                {
                    VolumeBootRecord vbr = VolumeBootRecord.Get(this.VolumePath);

                    AttributeList attrlist = attr as AttributeList;
                    foreach (AttrRef ar in attrlist.AttributeReference)
                    {
                        if (ar.Name == "DATA")
                        {
                            if (ar.NameString == "")
                            {
                                FileRecord record = new FileRecord(FileRecord.GetRecordBytes(this.VolumePath, (int)ar.RecordNumber), this.VolumePath, (int)vbr.BytesPerFileRecord, true);
                                return record.GetContent();
                            }
                        }
                    }
                }
            }
            throw new Exception("Could not locate file contents");
        }

        public byte[] GetContent(string StreamName)
        {
            foreach (FileRecordAttribute attr in this.Attribute)
            {
                if (attr.Name == FileRecordAttribute.ATTR_TYPE.DATA)
                {
                    if (attr.NameString == StreamName)
                    {
                        if (attr.NonResident)
                        {
                            return (attr as NonResident).GetBytes(this.VolumePath);
                        }
                        else
                        {
                            return (attr as Data).RawData;
                        }
                    }
                }
                /*else if (attr.Name == Attr.ATTR_TYPE.ATTRIBUTE_LIST)
                {
                    AttributeList attrlist = attr as AttributeList;
                    foreach (AttrRef ar in attrlist.AttributeReference)
                    {
                        if (ar.Name == "DATA")
                        {
                            if (attr.NameString == StreamName)
                            {
                                FileRecord record = new FileRecord(FileRecord.GetRecordBytes(this.VolumePath, (int)ar.RecordNumber), this.VolumePath, true);
                                return record.GetContent(StreamName);
                            }
                        }
                    }
                }*/
            }
            throw new Exception("Could not locate desired stream");
        }

        internal byte[] GetContent(VolumeBootRecord VBR)
        {
            foreach (FileRecordAttribute attr in this.Attribute)
            {
                if (attr.Name == FileRecordAttribute.ATTR_TYPE.DATA)
                {
                    if (attr.NonResident)
                    {
                        return (attr as NonResident).GetBytes(this.VolumePath, VBR);
                    }
                    else
                    {
                        return (attr as Data).RawData;
                    }
                }
                else if (attr.Name == FileRecordAttribute.ATTR_TYPE.ATTRIBUTE_LIST)
                {
                    AttributeList attrlist = attr as AttributeList;
                    foreach (AttrRef ar in attrlist.AttributeReference)
                    {
                        if (ar.Name == "DATA")
                        {
                            FileRecord record = new FileRecord(FileRecord.GetRecordBytes(this.VolumePath, (int)ar.RecordNumber), this.VolumePath, (int)VBR.BytesPerFileRecord, true);
                            return record.GetContent();
                        }
                    }
                }
            }
            throw new Exception("Could not locate file contents");
        }

        #endregion GetContentMethods

        #region GetTestContentMethods

        public byte[] GetTestContent()
        {
            return GetTestContent("");
        }

        public byte[] GetTestContent(string streamName)
        {
            foreach (FileRecordAttribute attr in this.Attribute)
            {
                if (attr.Name == FileRecordAttribute.ATTR_TYPE.DATA)
                {
                    if (attr.NameString.ToUpper() == streamName.ToUpper())
                    {
                        if (attr.NonResident)
                        {
                            return (attr as NonResident).GetBytes(this.VolumePath);
                        }
                        else
                        {
                            return (attr as Data).RawData;
                        }
                    }
                }

                AttributeList attrList = attr as AttributeList;
                if (attrList != null)
                {
                    VolumeBootRecord vbr = VolumeBootRecord.Get(this.VolumePath);

                    foreach (AttrRef ar in attrList.AttributeReference)
                    {
                        if (ar.Name == "DATA")
                        {
                            FileRecord record = new FileRecord(FileRecord.GetRecordBytes(this.VolumePath, (int)ar.RecordNumber), this.VolumePath, (int)vbr.BytesPerFileRecord, true);
                            return record.GetTestContent(streamName);
                        }
                    }
                }
            }
            throw new Exception("Could not locate desired stream");
        }

        #endregion GetTestContentMethods

        #region CopyFileMethods

        public void CopyFile(string Destination)
        {
            this.CopyFile(Destination, "");
        }

        public void CopyFile(string Destination, string StreamName)
        {
            byte[] fileBytes = this.GetContent(StreamName);

            // Open file for writing
            FileStream streamToWrite = new FileStream(Destination, System.IO.FileMode.Create, System.IO.FileAccess.Write);

            // Writes a block of bytes to this stream using data from a byte array.
            streamToWrite.Write(fileBytes, 0, fileBytes.Length);

            // Close file stream
            streamToWrite.Close();
        }

        #endregion CopyFileMethods

        #region GetChildMethods

        public IndexEntry[] GetChild()
        {
            return IndexEntry.GetInstances(this.FullName);
        }

        #endregion GetChildMethods

        #region GetParentMethods

        public FileRecord GetParent()
        {
            return FileRecord.Get(this.VolumePath, (int)this.ParentRecordNumber, false);
        }

        #endregion GetParentMethods

        #region GetUsnJrnlMethods

        public UsnJrnl GetUsnJrnl()
        {
            foreach (FileRecordAttribute attr in this.Attribute)
            {
                StandardInformation SI = attr as StandardInformation;
                if (SI != null)
                {
                    return UsnJrnl.Get(this.VolumePath, SI.UpdateSequenceNumber);

                }
            }
            throw new Exception("No $STANDARD_INFORMATION Attirbute found");
        }

        #endregion GetUsnJrnlMethods

        #region GetSlackMethods

        public byte[] GetSlack()
        {
            if (!(this.Directory))
            {
                if (this.Attribute != null)
                {
                    foreach (FileRecordAttribute attr in this.Attribute)
                    {
                        if (attr.Name == FileRecordAttribute.ATTR_TYPE.DATA)
                        {
                            if (attr.NonResident)
                            {
                                return (attr as NonResident).GetSlack(this.VolumePath);
                            }
                            else
                            {
                                return null;
                            }
                        }
                        /*else if (attr.Name == Attr.ATTR_TYPE.ATTRIBUTE_LIST)
                        {
                            AttributeList attrlist = attr as AttributeList;
                            foreach (AttrRef ar in attrlist.AttributeReference)
                            {
                                if (ar.Name == "DATA")
                                {
                                    FileRecord record = new FileRecord(FileRecord.GetRecordBytes(this.VolumePath, (int)ar.RecordNumber), this.VolumePath, true);
                                    return record.GetSlack();
                                }
                            }
                        }*/
                    }
                }
            }
            return null;
        }

        #endregion GetSlackMethods

        #region GetMftSlackMethods

        public byte[] GetMftSlack()
        {
            byte[] bytes = FileRecord.GetRecordBytes(this.VolumePath, (int)this.RecordNumber);
            return Helper.GetSubArray(bytes, this.RealSize - 1, this.AllocatedSize - this.RealSize);
        }

        #endregion GetMftSlackMethods

        #region ToStringOverride

        public override string ToString()
        {
            if (this.Directory)
            {
                if (this.Deleted)
                {
                    return String.Format("[Directory] {0} (deleted)", this.FullName);
                }
                else
                {
                    return String.Format("[Directory] {0}", this.FullName);
                }
            }
            else
            {
                ulong size = 0;

                foreach (FileRecordAttribute a in this.Attribute)
                {
                    if (a.Name == FileRecordAttribute.ATTR_TYPE.DATA)
                    {
                        if (a.NonResident)
                        {
                            NonResident d = a as NonResident;
                            size = d.RealSize;
                            break;
                        }
                        else
                        {
                            Data data = a as Data;
                            size = (ulong)data.RawData.Length;
                            break;
                        }
                    }
                }

                if (this.Deleted)
                {
                    return String.Format("[{0}] {1} (deleted)", size, this.FullName);
                }
                else
                {
                    return String.Format("[{0}] {1}", size, this.FullName);
                }

            }
        }

        #endregion ToStringOverride

        #endregion InstanceMethods

        #region PrivateMethods

        private static void ApplyFixup(ref byte[] bytes, int BytesPerFileRecord)
        {
            int offset = 0x00;

            while (offset < bytes.Length)
            {
                // Take UpdateSequence into account
                ushort usoffset = BitConverter.ToUInt16(bytes, 0x04 + offset);
                ushort ussize = BitConverter.ToUInt16(bytes, 0x06 + offset);

                if (ussize != 0)
                {
                    byte[] UpdateSequenceArray = Helper.GetSubArray(bytes, (usoffset + 0x02 + offset), (0x02 * ussize));

                    bytes[0x1FE + offset] = UpdateSequenceArray[0x00];
                    bytes[0x1FF + offset] = UpdateSequenceArray[0x01];
                    bytes[0x3FE + offset] = UpdateSequenceArray[0x02];
                    bytes[0x3FF + offset] = UpdateSequenceArray[0x03];
                }

                offset += BytesPerFileRecord;
            }
        }

        private static bool isDeleted(FILE_RECORD_FLAG flags)
        {
            if ((flags & FILE_RECORD_FLAG.INUSE) == FILE_RECORD_FLAG.INUSE)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        private static bool isDirectory(FILE_RECORD_FLAG flags)
        {
            if ((flags & FILE_RECORD_FLAG.DIR) == FILE_RECORD_FLAG.DIR)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        #endregion PrivateMethods
    }
}
