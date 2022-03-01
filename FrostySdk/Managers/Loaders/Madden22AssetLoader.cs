using FrostySdk.Interfaces;
using FrostySdk.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FrostySdk.Managers
{
    public partial class AssetManager
    {
        internal class Madden22AssetLoader : IAssetLoader
        {
            internal class BundleFileInfoEntry
            {
                public int Unk { get; set; }
                public bool IsPatch { get; set; }
                public int Catalog { get; set; }
                public int Cas { get; set; }
                public int CasOffset { get; set; }
                public int Size { get; set; }
            }

            internal class BundleFileInfo
            {
                public int Unk { get; set; }
                public long Offset { get; set; }
                public int Size { get; set; }
                public List<BundleFileInfoEntry> Entries { get; set; } = new List<BundleFileInfoEntry>();
            }

            internal class ChunkGuidEntry
            {
                public Guid Guid { get; set; }
                public byte Flag { get; set; }
                public int IndexRaw { get; set; }
                public int Index
                {
                    get
                    {
                        return IndexRaw / 3;
                    }
                }
            }

            internal class RawChunkAssetEntry
            {
                public byte Unk { get; set; }
                public bool IsPatch { get; set; }
                public byte Catalog { get; set; }
                public byte Cas { get; set; }
                public uint ChunkOffset { get; set; }
                public uint ChunkSize { get; set; }
                public Guid Guid { get; set; }
            }

            internal class SBTocReader : NativeReader
            {
                long dataStartOffset = 0;

                int bundleReferencesOffset = 0;
                int bundlesOffset = 0;
                int bundlesCount = 0;

                int chunksFlagOffset = 0;
                int chunksGuidOffset = 0;
                int chunksCount = 0;
                int chunksEntryOffset = 0;

                int unk1Count = 0;
                int unk2Count = 0;

                List<uint> bundleReferences;
                List<ChunkGuidEntry> chunkGuidEntries;

                public List<BundleFileInfo> BundleFileInfos { get; set; } = new List<BundleFileInfo>();
                public List<RawChunkAssetEntry> RawChunkAssetEntries { get; set; } = new List<RawChunkAssetEntry>();

                public SBTocReader(Stream inStream, IDeobfuscator inDeobfuscator)
                    : base(inStream, inDeobfuscator)
                {
                }

                public void ReadHeader()
                {
                    dataStartOffset = Position;

                    bundleReferencesOffset = ReadInt(Endian.Big);
                    bundlesOffset = ReadInt(Endian.Big);
                    bundlesCount = ReadInt(Endian.Big);

                    chunksFlagOffset = ReadInt(Endian.Big);
                    chunksGuidOffset = ReadInt(Endian.Big);
                    chunksCount = ReadInt(Endian.Big);
                    chunksEntryOffset = ReadInt(Endian.Big);
                }

                public void ReadBundleReferences()
                {
                    bundleReferences = new List<uint>();
                    Position = dataStartOffset + bundleReferencesOffset;

                    for (var i = 0; i < bundlesCount; i++)
                    {
                        bundleReferences.Add(ReadUInt(Endian.Big));
                    }
                }

                public void ReadBundleFileInfos()
                {
                    BundleFileInfos = new List<BundleFileInfo>();
                    Position = dataStartOffset + bundlesOffset;

                    for (var i = 0; i < bundlesCount; i++)
                    {
                        BundleFileInfos.Add(
                            new BundleFileInfo
                            {
                                Unk = ReadInt(Endian.Big),
                                Size = ReadInt(Endian.Big) & 0xFFFFFFF,         // All sizes start with 0x40. Some unknown flag.
                                Offset = ReadLong(Endian.Big)
                            }
                        );
                    }

                    List<byte> flags;

                    foreach (var currentBundleInfo in BundleFileInfos)
                    {
                        flags = new List<byte>();
                        Position = dataStartOffset + currentBundleInfo.Offset + 8;

                        int flagsOffset = ReadInt(Endian.Big);
                        int entriesCount = ReadInt(Endian.Big);
                        int entriesOffset = ReadInt(Endian.Big);

                        Position = dataStartOffset + currentBundleInfo.Offset + flagsOffset;
                        for (var j = 0; j < entriesCount; j++)
                        {
                            flags.Add(ReadByte());
                        }

                        byte unk = 0, catalog = 0, cas = 0;
                        bool isPatch = false;

                        Position = dataStartOffset + currentBundleInfo.Offset + entriesOffset;
                        for (var j = 0; j < entriesCount; j++)
                        {
                            var entry = new BundleFileInfoEntry();

                            if (flags[j] == 1)
                            {
                                entry.Unk = unk = ReadByte();
                                entry.IsPatch = isPatch = ReadBoolean();
                                entry.Catalog = catalog = ReadByte();
                                entry.Cas = cas = ReadByte();
                            }
                            else
                            {
                                entry.Unk = unk;
                                entry.IsPatch = isPatch;
                                entry.Catalog = catalog;
                                entry.Cas = cas;
                            }

                            entry.CasOffset = ReadInt(Endian.Big);
                            entry.Size = ReadInt(Endian.Big);

                            currentBundleInfo.Entries.Add(entry);
                        }
                    }
                }
                
                public void ReadChunks()
                {
                    ReadChunkGuids();
                    ReadChunkEntries();

                    foreach (var entry in chunkGuidEntries)
                    {
                        RawChunkAssetEntries[entry.Index].Guid = entry.Guid;
                    }
                }

                private void ReadChunkGuids()
                {
                    chunkGuidEntries = new List<ChunkGuidEntry>();
                    Position = dataStartOffset + chunksGuidOffset;

                    for (var i = 0; i < chunksCount; i++)
                    {
                        byte[] bytes = ReadBytes(0x10);
                        Array.Reverse(bytes);

                        Guid guid = new Guid(bytes);
                        uint flags = ReadUInt(Endian.Big);

                        ChunkGuidEntry entry = new ChunkGuidEntry()
                        {
                            Guid = guid,
                            Flag = (byte)((flags & 0xFF000000) >> 24),
                            IndexRaw = (int)flags & 0xFFFFFF
                        };

                        chunkGuidEntries.Add(entry);
                    }
                }

                private void ReadChunkEntries()
                {
                    Position = dataStartOffset + chunksEntryOffset;

                    for (var i = 0; i < chunksCount; i++)
                    {
                        RawChunkAssetEntries.Add
                        (
                            new RawChunkAssetEntry()
                            {
                                Unk = ReadByte(),
                                IsPatch = ReadBoolean(),
                                Catalog = ReadByte(),
                                Cas = ReadByte(),
                                ChunkOffset = ReadUInt(Endian.Big),
                                ChunkSize = ReadUInt(Endian.Big)
                            }
                        );
                    }
                }
            }

            public void Load(AssetManager parent, BinarySbDataHelper helper)
            {
                foreach (string sbName in parent.fs.SuperBundles)
                {
                    SuperBundleEntry sbe = parent.superBundles.Find((SuperBundleEntry a) => a.Name == sbName);
                    int sbIndex = -1;

                    if (sbe != null)
                    {
                        sbIndex = parent.superBundles.IndexOf(sbe);
                    }
                    else
                    {
                        parent.superBundles.Add(new SuperBundleEntry { Name = sbName });
                        sbIndex = parent.superBundles.Count - 1;
                    }

                    parent.WriteToLog("Loading data ({0})", sbName);
                    string patchPath = parent.fs.ResolvePath(string.Format("native_patch/{0}.toc", sbName));
                    string dataPath = parent.fs.ResolvePath(string.Format("native_data/{0}.toc", sbName));

                    if (patchPath != "")
                    {
                        ReadSuperbundle(patchPath, parent, helper, sbIndex);
                    }

                    if (dataPath != "")
                    {
                        ReadSuperbundle(dataPath, parent, helper, sbIndex);
                    }
                }
            }

            private void ReadSuperbundle(string tocPath, AssetManager parent, BinarySbDataHelper helper, int sbIndex)
            {
                List<BundleFileInfo> bundleFileInfos = new List<BundleFileInfo>();

                using (SBTocReader reader = new SBTocReader(new FileStream(tocPath, FileMode.Open, FileAccess.Read), parent.fs.CreateDeobfuscator()))
                {
                    reader.ReadHeader();
                    reader.ReadBundleReferences();
                    reader.ReadBundleFileInfos();
                    bundleFileInfos = reader.BundleFileInfos;

                    reader.ReadChunks();

                    foreach (var entry in reader.RawChunkAssetEntries)
                    {
                        var newChunkAssetEntry = new ChunkAssetEntry()
                        {
                            Id = entry.Guid,
                            IsTocChunk = true,
                            Size = entry.ChunkSize,
                            Location = AssetDataLocation.CasNonIndexed,
                            ExtraData = new AssetExtraData()
                            {
                                CasPath = parent.fs.GetFilePath(entry.Catalog, entry.Cas, entry.IsPatch),
                                DataOffset = entry.ChunkOffset
                            }
                        };

                        if (!parent.chunkList.TryGetValue(entry.Guid, out var chunkEntry))
                        {
                            parent.chunkList[entry.Guid] = newChunkAssetEntry;
                        }
                        else
                        {
                            chunkEntry.Id = newChunkAssetEntry.Id;
                            chunkEntry.Size = newChunkAssetEntry.Size;
                            chunkEntry.Location = newChunkAssetEntry.Location;
                            chunkEntry.ExtraData = newChunkAssetEntry.ExtraData;
                            chunkEntry.IsTocChunk = newChunkAssetEntry.IsTocChunk;
                        }
                    }
                }

                int currentBundleIndex = 0;

                foreach (var currentBundleInfo in bundleFileInfos)
                {
                    parent.logger.Log("progress:{0}", (currentBundleIndex / (double)bundleFileInfos.Count) * 100.0d);

                    MemoryStream ms = new MemoryStream();

                    var bundleEntry = currentBundleInfo.Entries[0];

                    DbObject bundle;

                    var casFilePath = parent.fs.GetFilePath(bundleEntry.Catalog, bundleEntry.Cas, bundleEntry.IsPatch);

                    using (NativeReader casReader = new NativeReader(new FileStream(parent.fs.ResolvePath(casFilePath), FileMode.Open, FileAccess.Read)))
                    {
                        casReader.Position = bundleEntry.CasOffset;
                        ms.Write(casReader.ReadBytes(bundleEntry.Size), 0, bundleEntry.Size);
                    }

                    using (BinarySbReader bundleReader = new BinarySbReader(ms, 0, parent.fs.CreateDeobfuscator()))
                    {
                        bundleReader.binarySbReader.Endian = Endian.Little;
                        bundle = bundleReader.ReadDbObject();
                    }

                    DbObject ebxs = bundle.GetValue<DbObject>("ebx");

                    if (ebxs.Count > 0)
                    {
                        DbObject lastEbx = (DbObject)ebxs[ebxs.Count - 1];
                        BundleEntry be = new BundleEntry { Name = lastEbx.GetValue<string>("name"), SuperBundleId = sbIndex };
                        parent.bundles.Add(be);
                    }

                    List<DbObject> objects = new List<DbObject>();
                    foreach (DbObject ebx in bundle.GetValue<DbObject>("ebx"))
                    {
                        objects.Add(ebx);
                    }
                    foreach (DbObject res in bundle.GetValue<DbObject>("res"))
                    {
                        objects.Add(res);
                    }
                    foreach (DbObject chunk in bundle.GetValue<DbObject>("chunks"))
                    {
                        objects.Add(chunk);
                    }

                    int objectIndex = 1;    // Start at 1 because the bundle entry is always the 0th index.

                    foreach (DbObject obj in objects)
                    {
                        var entry = currentBundleInfo.Entries[objectIndex];

                        obj.SetValue("catalog", entry.Catalog);
                        obj.SetValue("cas", entry.Cas);
                        obj.SetValue("offset", entry.CasOffset);
                        obj.SetValue("size", entry.Size);

                        if (entry.IsPatch)
                        {
                            obj.SetValue("patch", true);
                        }

                        objectIndex++;
                    }

                    parent.ProcessBundleEbx(bundle, parent.bundles.Count - 1, helper);
                    parent.ProcessBundleRes(bundle, parent.bundles.Count - 1, helper);
                    parent.ProcessBundleChunks(bundle, parent.bundles.Count - 1, helper);

                    currentBundleIndex += 1;
                }
            }
        }
    }
}
