﻿using CASCLib;
using DBCD.Providers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using WoWFormatLib.FileReaders;

namespace WoWExportTools.Exporters.OBJ
{
    public class ADTExporter
    {
        public static void ExportADT(uint wdtFileDataID, byte tileX, byte tileY, BackgroundWorker exportworker = null)
        {
            if (exportworker == null)
            {
                exportworker = new BackgroundWorker();
                exportworker.WorkerReportsProgress = true;
            }

            var outdir = ConfigurationManager.AppSettings["outdir"];

            var customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
            customCulture.NumberFormat.NumberDecimalSeparator = ".";
            System.Threading.Thread.CurrentThread.CurrentCulture = customCulture;

            var MaxSize = 51200 / 3.0;
            var TileSize = MaxSize / 32.0;
            var ChunkSize = TileSize / 16.0;
            var UnitSize = ChunkSize / 8.0;
            var UnitSizeHalf = UnitSize / 2.0;

            if (!Listfile.TryGetFilename(wdtFileDataID, out string wdtFilename))
            {
                Logger.WriteLine("ADT OBJ Exporter: WDT {0} has no known filename, skipping export!", wdtFileDataID);
                return;
            }

            var mapName = Path.GetFileNameWithoutExtension(wdtFilename);
            var file = "world/maps/" + mapName + "/" + mapName + "_" + tileX.ToString() + "_" + tileY.ToString() + ".adt";

            var reader = new ADTReader();
            reader.LoadADT(wdtFileDataID, tileX, tileY, true, wdtFilename);

            var adt = reader.adtfile;

            if (adt.chunks == null)
            {
                Logger.WriteLine("ADT OBJ Exporter: File {0} has no chunks, skipping export!", file);
                return;
            }

            Logger.WriteLine("ADT OBJ Exporter: Starting export of {0}..", file);

            Directory.CreateDirectory(Path.Combine(outdir, Path.GetDirectoryName(file)));

            exportworker.ReportProgress(0, "Loading ADT " + file);

            var renderBatches = new List<Structs.RenderBatch>();
            var verticelist = new List<Structs.Vertex>();
            var indicelist = new List<int>();
            var materials = new Dictionary<int, string>();

            ConfigurationManager.RefreshSection("appSettings");
            var bakeQuality = ConfigurationManager.AppSettings["bakeQuality"];

            // Calculate ADT offset in world coordinates
            var adtStartX = ((adt.x - 32) * TileSize) * -1;
            var adtStartY = ((adt.y - 32) * TileSize) * -1;

            // Calculate first chunk offset in world coordinates
            var initialChunkX = adtStartY + (adt.chunks[0].header.indexX * ChunkSize) * -1;
            var initialChunkY = adtStartX + (adt.chunks[0].header.indexY * ChunkSize) * -1;

            uint ci = 0;
            for (var x = 0; x < 16; x++)
            {
                double xOfs = x / 16d;
                for (var y = 0; y < 16; y++)
                {
                    double yOfs = y / 16d;

                    var genx = (initialChunkX + (ChunkSize * x) * -1);
                    var geny = (initialChunkY + (ChunkSize * y) * -1);

                    var chunk = adt.chunks[ci];

                    var off = verticelist.Count();

                    var batch = new Structs.RenderBatch();

                    for (int i = 0, idx = 0; i < 17; i++)
                    {
                        bool isSmallRow = (i % 2) != 0;
                        int rowLength = isSmallRow ? 8 : 9;

                        for (var j = 0; j < rowLength; j++)
                        {
                            var v = new Structs.Vertex();

                            v.Normal = new Structs.Vector3D
                            {
                                X = (double)chunk.normals.normal_0[idx] / 127,
                                Y = (double)chunk.normals.normal_2[idx] / 127,
                                Z = (double)chunk.normals.normal_1[idx] / 127
                            };

                            var px = geny - (j * UnitSize);
                            var py = chunk.vertices.vertices[idx++] + chunk.header.position.Z;
                            var pz = genx - (i * UnitSizeHalf);

                            v.Position = new Structs.Vector3D
                            {
                                X = px,
                                Y = py,
                                Z = pz
                            };

                            if ((i % 2) != 0) v.Position.X = (px - UnitSizeHalf);

                            double ofs = j;
                            if (isSmallRow)
                                ofs += 0.5;

                            if (bakeQuality == "none")
                            {
                                v.TexCoord = new Structs.Vector2D { X = (j + (((i % 2) != 0) ? 0.5f : 0f)) / 8f, Y = (i * 0.5f) / 8f };
                            }
                            else if (bakeQuality == "high")
                            {
                                double tx = ofs / 8d;
                                double ty = 1 - (i / 16d);
                                v.TexCoord = new Structs.Vector2D { X = tx, Y = ty };
                            }
                            else
                            {
                                double tx = -(v.Position.X - initialChunkY) / TileSize;
                                double ty = (v.Position.Z - initialChunkX) / TileSize;

                                v.TexCoord = new Structs.Vector2D { X = tx, Y = ty };
                            }
                            verticelist.Add(v);
                        }
                    }

                    batch.firstFace = (uint)indicelist.Count();

                    // Stupid C# and its structs
                    var holesHighRes = new byte[8];
                    holesHighRes[0] = chunk.header.holesHighRes_0;
                    holesHighRes[1] = chunk.header.holesHighRes_1;
                    holesHighRes[2] = chunk.header.holesHighRes_2;
                    holesHighRes[3] = chunk.header.holesHighRes_3;
                    holesHighRes[4] = chunk.header.holesHighRes_4;
                    holesHighRes[5] = chunk.header.holesHighRes_5;
                    holesHighRes[6] = chunk.header.holesHighRes_6;
                    holesHighRes[7] = chunk.header.holesHighRes_7;

                    for (int j = 9, xx = 0, yy = 0; j < 145; j++, xx++)
                    {
                        if (xx >= 8) { xx = 0; ++yy; }
                        var isHole = true;

                        // Check if chunk is using low-res holes
                        if ((chunk.header.flags & 0x10000) == 0)
                        {
                            // Calculate current hole number
                            var currentHole = (int)Math.Pow(2,
                                    Math.Floor(xx / 2f) * 1f +
                                    Math.Floor(yy / 2f) * 4f);

                            // Check if current hole number should be a hole
                            if ((chunk.header.holesLowRes & currentHole) == 0)
                            {
                                isHole = false;
                            }
                        }

                        else
                        {
                            // Check if current section is a hole
                            if (((holesHighRes[yy] >> xx) & 1) == 0)
                            {
                                isHole = false;
                            }
                        }

                        if (!isHole)
                        {
                            indicelist.AddRange(new int[] { off + j + 8, off + j - 9, off + j });
                            indicelist.AddRange(new int[] { off + j - 9, off + j - 8, off + j });
                            indicelist.AddRange(new int[] { off + j - 8, off + j + 9, off + j });
                            indicelist.AddRange(new int[] { off + j + 9, off + j + 8, off + j });

                            // Generates quads instead of 4x triangles
                            //indicelist.AddRange(new int[] { off + j + 8, off + j - 9, off + j - 8 });
                            //indicelist.AddRange(new int[] { off + j - 8, off + j + 9, off + j + 8 });

                        }

                        if ((j + 1) % (9 + 8) == 0) j += 9;
                    }

                    if (bakeQuality == "high")
                    {
                        materials.Add((int)ci + 1, Path.GetFileNameWithoutExtension(file).Replace(" ", "") + "_" + ci);
                        batch.materialID = ci + 1;
                    }
                    else
                    {
                        if (!materials.ContainsKey(1))
                        {
                            materials.Add(1, Path.GetFileNameWithoutExtension(file).Replace(" ", ""));
                        }
                        batch.materialID = (uint)materials.Count();
                    }

                    batch.numFaces = (uint)(indicelist.Count()) - batch.firstFace;

                    if (bakeQuality == "none")
                    {
                        // Build alpha textures, export raw and height textures
                        var rawMaterials = new List<Renderer.Structs.Material>();

                        if (adt.textures.filenames == null)
                        {
                            for (var ti = 0; ti < adt.diffuseTextureFileDataIDs.Count(); ti++)
                            {
                                var material = new Renderer.Structs.Material();
                                material.filename = adt.diffuseTextureFileDataIDs[ti].ToString();
                                material.textureID = (int)adt.diffuseTextureFileDataIDs[ti];

                                if (adt.texParams != null && adt.texParams.Count() >= ti)
                                {
                                    material.scale = (float)Math.Pow(2, (adt.texParams[ti].flags & 0xF0) >> 4);
                                    if (adt.texParams[ti].height != 0.0 || adt.texParams[ti].offset != 1.0)
                                    {
                                        material.heightScale = adt.texParams[ti].height;
                                        material.heightOffset = adt.texParams[ti].offset;

                                        if (!WoWFormatLib.Utils.CASC.FileExists(adt.heightTextureFileDataIDs[ti]))
                                        {
                                            Console.WriteLine("Height texture: " + adt.heightTextureFileDataIDs[ti] + " does not exist! Falling back to original texture (hack)..");
                                            material.heightTexture = (int)adt.diffuseTextureFileDataIDs[ti];
                                        }
                                        else
                                        {
                                            material.heightTexture = (int)adt.heightTextureFileDataIDs[ti];
                                        }
                                    }
                                    else
                                    {
                                        material.heightScale = 0.0f;
                                        material.heightOffset = 1.0f;
                                    }
                                }
                                else
                                {
                                    material.heightScale = 0.0f;
                                    material.heightOffset = 1.0f;
                                    material.scale = 1.0f;
                                }
                                rawMaterials.Add(material);
                            }
                        }
                        else
                        {
                            for (var ti = 0; ti < adt.textures.filenames.Count(); ti++)
                            {
                                var material = new Renderer.Structs.Material();
                                material.filename = adt.textures.filenames[ti];
                                material.textureID = (int)WoWFormatLib.Utils.CASC.getFileDataIdByName(adt.textures.filenames[ti]);

                                if (adt.texParams != null && adt.texParams.Count() >= ti)
                                {
                                    material.scale = (float)Math.Pow(2, (adt.texParams[ti].flags & 0xF0) >> 4);
                                    if (adt.texParams[ti].height != 0.0 || adt.texParams[ti].offset != 1.0)
                                    {
                                        material.heightScale = adt.texParams[ti].height;
                                        material.heightOffset = adt.texParams[ti].offset;

                                        var heightName = adt.textures.filenames[ti].Replace(".blp", "_h.blp");
                                        if (!WoWFormatLib.Utils.CASC.FileExists(heightName))
                                        {
                                            Console.WriteLine("Height texture: " + heightName + " does not exist! Falling back to original texture (hack)..");
                                            material.heightTexture = (int)WoWFormatLib.Utils.CASC.getFileDataIdByName(adt.textures.filenames[ti]);
                                        }
                                        else
                                        {
                                            material.heightTexture = (int)WoWFormatLib.Utils.CASC.getFileDataIdByName(heightName);
                                        }
                                    }
                                    else
                                    {
                                        material.heightScale = 0.0f;
                                        material.heightOffset = 1.0f;
                                    }
                                }
                                else
                                {
                                    material.heightScale = 0.0f;
                                    material.heightOffset = 1.0f;
                                    material.scale = 1.0f;
                                }
                                rawMaterials.Add(material);
                            }
                        }

                        var layerMaterials = new List<uint>();
                        var alphalayermats = new List<int>();
                        var layerscales = new List<float>();
                        var layerheights = new List<int>();

                        //batch.heightScales = new Vector4();
                        //batch.heightOffsets = new Vector4();
                        exportworker.ReportProgress(10, "Exporting alpha layers " + file);

                        var baseTextureName = Path.Combine(outdir, Path.GetDirectoryName(file), "terraindiffuse", Path.GetFileNameWithoutExtension(file).Replace(" ", ""));
                        var baseAlphaTextureName = Path.Combine(outdir, Path.GetDirectoryName(file), "terrainalpha", Path.GetFileNameWithoutExtension(file).Replace(" ", ""));
                        Directory.CreateDirectory(Path.GetDirectoryName(baseAlphaTextureName));

                        var pixels = new byte[4, 4096];

                        for (var li = 0; li < adt.texChunks[ci].layers.Count(); li++)
                        {
                            if (adt.texChunks[ci].alphaLayer != null)
                            {
                                var values = adt.texChunks[ci].alphaLayer[li].layer;
                                for (var tx = 0; tx < 64; tx++)
                                {
                                    for (var ty = 0; ty < 64; ty++)
                                    {
                                        pixels[li, tx * 64 + ty] = values[tx * 64 + ty];

                                    }
                                }
                            }

                            Renderer.Structs.Material curMat;

                            if (adt.diffuseTextureFileDataIDs == null)
                            {
                                if (adt.textures.filenames == null)
                                    throw new Exception("ADT has no textures?");

                                var texFileDataID = WoWFormatLib.Utils.CASC.getFileDataIdByName(adt.textures.filenames[adt.texChunks[ci].layers[li].textureId]);

                                //layerMaterials.Add((uint)BLPLoader.LoadTexture(texFileDataID));
                                curMat = rawMaterials.Where(material => material.filename == adt.textures.filenames[adt.texChunks[ci].layers[li].textureId]).Single();

                            }
                            else
                            {
                                //layerMaterials.Add((uint)BLPLoader.LoadTexture(adt.diffuseTextureFileDataIDs[adt.texChunks[ci].layers[li].textureId]));
                                curMat = rawMaterials.Where(material => material.filename == adt.diffuseTextureFileDataIDs[adt.texChunks[ci].layers[li].textureId].ToString()).Single();
                                //Console.WriteLine(ci + " " + li + " " + curMat.filename);
                            }

                            //layerscales.Add(curMat.scale);
                            //layerheights.Add(curMat.heightTexture);

                            //batch.heightScales[li] = curMat.heightScale;
                            //batch.heightOffsets[li] = curMat.heightOffset;
                        }

                        //batch.materialID = layerMaterials.ToArray();
                        //batch.alphaMaterialID = alphalayermats.ToArray();
                        //batch.scales = layerscales.ToArray();
                        //batch.heightMaterialIDs = layerheights.ToArray();

                        using (var bmp = new System.Drawing.Bitmap(64, 64))
                        {
                            for (var tx = 0; tx < 64; tx++)
                            {
                                for (var ty = 0; ty < 64; ty++)
                                {
                                    var color = System.Drawing.Color.FromArgb(pixels[0, tx * 64 + ty], pixels[1, tx * 64 + ty], pixels[2, tx * 64 + ty], pixels[3, tx * 64 + ty]);
                                    bmp.SetPixel(ty, tx, color);
                                }
                            }

                            bmp.Save(baseAlphaTextureName + "_" + ci + ".png", System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }

                    renderBatches.Add(batch);
                    ci++;
                }
            }

            ConfigurationManager.RefreshSection("appSettings");

            bool exportWMO = ConfigurationManager.AppSettings["exportWMO"] == "True";
            bool exportM2 = ConfigurationManager.AppSettings["exportM2"] == "True";
            bool exportFoliage = ConfigurationManager.AppSettings["exportFoliage"] == "True";

            if (exportFoliage)
            {
                exportworker.ReportProgress(65, "Exporting ADT foliage");

                try
                {
                    var build = WoWFormatLib.Utils.CASC.BuildName;
                    var dbcd = new DBCD.DBCD(new DBC.CASCDBCProvider(), new GithubDBDProvider());
                    var groundEffectTextureDB = dbcd.Load("GroundEffectTexture");
                    var groundEffectDoodadDB = dbcd.Load("GroundEffectDoodad");
                    for (var c = 0; c < reader.adtfile.texChunks.Length; c++)
                    {
                        for (var l = 0; l < reader.adtfile.texChunks[c].layers.Length; l++)
                        {
                            var effectID = reader.adtfile.texChunks[c].layers[l].effectId;
                            if (effectID == 0)
                                continue;

                            if (!groundEffectTextureDB.ContainsKey(effectID))
                            {
                                continue;
                            }

                            dynamic textureEntry = groundEffectTextureDB[effectID];
                            foreach (int doodad in textureEntry.DoodadID)
                            {
                                if (!groundEffectDoodadDB.ContainsKey(doodad))
                                {
                                    continue;
                                }

                                dynamic doodadEntry = groundEffectDoodadDB[doodad];

                                var filedataid = (uint)doodadEntry.ModelFileID;

                                if (!Listfile.TryGetFilename(filedataid, out var filename))
                                {
                                    Logger.WriteLine("Could not find filename for " + filedataid + ", setting filename to filedataid..");
                                    filename = filedataid.ToString();
                                }

                                if (!File.Exists(Path.Combine(outdir, Path.GetDirectoryName(file), "foliage")))
                                {
                                    Directory.CreateDirectory(Path.Combine(outdir, Path.GetDirectoryName(file), "foliage"));
                                }

                                if (!File.Exists(Path.Combine(outdir, Path.GetDirectoryName(file), "foliage", Path.GetFileNameWithoutExtension(filename).ToLower() + ".obj")))
                                {
                                    M2Exporter.ExportM2(filedataid, null, Path.Combine(outdir, Path.GetDirectoryName(file), "foliage"), filename);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.WriteLine("Error exporting GroundEffects: " + e.Message);
                }
            }

            if (exportWMO || exportM2)
            {
                var doodadSW = new StreamWriter(Path.Combine(outdir, Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file).Replace(" ", "") + "_ModelPlacementInformation.csv"));
                doodadSW.WriteLine("ModelFile;PositionX;PositionY;PositionZ;RotationX;RotationY;RotationZ;ScaleFactor;ModelId;Type");

                if (exportWMO)
                {
                    exportworker.ReportProgress(25, "Exporting ADT worldmodels");

                    for (var mi = 0; mi < reader.adtfile.objects.worldModels.entries.Count(); mi++)
                    {
                        var wmo = reader.adtfile.objects.worldModels.entries[mi];

                        var filename = "";
                        uint filedataid = 0;

                        if (reader.adtfile.objects.wmoNames.filenames == null)
                        {
                            filedataid = wmo.mwidEntry;
                            if (!Listfile.TryGetFilename(filedataid, out filename))
                            {
                                Logger.WriteLine("Warning! Could not find filename for " + filedataid + ", setting filename to filedataid..");
                                filename = filedataid.ToString() + ".wmo";
                            }
                        }
                        else
                        {
                            Logger.WriteLine("Warning!! File " + filename + " ID: " + filedataid + " still has filenames!");
                            filename = reader.adtfile.objects.wmoNames.filenames[wmo.mwidEntry];
                            if (!Listfile.TryGetFileDataID(filename, out filedataid))
                            {
                                Logger.WriteLine("Error! Could not find filedataid for " + filename + "!");
                                continue;
                            }
                        }

                        short doodadSet = -1;
                        if (ConfigurationManager.AppSettings["exportWMODoodads"] == "True")
                            doodadSet = (short)wmo.doodadSet;

                        if (string.IsNullOrEmpty(filename))
                        {
                            string wmoFile = Path.Combine(outdir, Path.GetDirectoryName(file), filedataid.ToString() + ".obj");
                            if (!File.Exists(wmoFile))
                                WMOExporter.ExportWMO(filedataid, exportworker, Path.Combine(outdir, Path.GetDirectoryName(file)), doodadSet);

                            if (File.Exists(wmoFile))
                                doodadSW.WriteLine(filedataid + ".obj;" + wmo.position.X + ";" + wmo.position.Y + ";" + wmo.position.Z + ";" + wmo.rotation.X + ";" + wmo.rotation.Y + ";" + wmo.rotation.Z + ";" + wmo.scale / 1024f + ";" + wmo.uniqueId + ";wmo");
                        }
                        else
                        {
                            string wmoFile = Path.Combine(outdir, Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(filename).ToLower() + ".obj");
                            if (!File.Exists(wmoFile))
                                WMOExporter.ExportWMO(filedataid, exportworker, Path.Combine(outdir, Path.GetDirectoryName(file)), doodadSet, filename);

                            if (File.Exists(Path.Combine(outdir, Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(filename).ToLower() + ".obj")))
                                doodadSW.WriteLine(Path.GetFileNameWithoutExtension(filename).ToLower() + ".obj;" + wmo.position.X + ";" + wmo.position.Y + ";" + wmo.position.Z + ";" + wmo.rotation.X + ";" + wmo.rotation.Y + ";" + wmo.rotation.Z + ";" + wmo.scale / 1024f + ";" + wmo.uniqueId + ";wmo");
                        }
                    }
                }

                if (exportM2)
                {
                    exportworker.ReportProgress(50, "Exporting ADT doodads");

                    for (var mi = 0; mi < reader.adtfile.objects.models.entries.Count(); mi++)
                    {
                        var doodad = reader.adtfile.objects.models.entries[mi];

                        string filename;
                        uint filedataid;

                        if (reader.adtfile.objects.m2Names.filenames == null)
                        {
                            filedataid = doodad.mmidEntry;
                            if (!Listfile.TryGetFilename(filedataid, out filename))
                            {
                                Logger.WriteLine("Could not find filename for " + filedataid + ", setting filename to filedataid..");
                                filename = filedataid.ToString();
                            }
                        }
                        else
                        {
                            filename = reader.adtfile.objects.m2Names.filenames[doodad.mmidEntry].ToLower();
                            if (!Listfile.TryGetFileDataID(filename, out filedataid))
                            {
                                Logger.WriteLine("Error! Could not find filedataid for " + filename + "!");
                                continue;
                            }
                        }

                        if (!File.Exists(Path.Combine(outdir, Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(filename) + ".obj")))
                        {
                            M2Exporter.ExportM2(filedataid, null, Path.Combine(outdir, Path.GetDirectoryName(file)), filename);
                        }

                        if (File.Exists(Path.Combine(outdir, Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(filename) + ".obj")))
                        {
                            doodadSW.WriteLine(Path.GetFileNameWithoutExtension(filename) + ".obj;" + doodad.position.X + ";" + doodad.position.Y + ";" + doodad.position.Z + ";" + doodad.rotation.X + ";" + doodad.rotation.Y + ";" + doodad.rotation.Z + ";" + doodad.scale / 1024f + ";" + doodad.uniqueId + ";m2");
                        }
                    }
                }

                doodadSW.Close();
            }

            exportworker.ReportProgress(75, "Exporting terrain textures..");

            if (bakeQuality != "none")
            {
                var mtlsw = new StreamWriter(Path.Combine(outdir, Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file).Replace(" ", "") + ".mtl"));

                //No idea how MTL files really work yet. Needs more investigation.
                foreach (var material in materials)
                {
                    mtlsw.WriteLine("newmtl " + material.Value.Replace(" ", ""));
                    mtlsw.WriteLine("Ka 1.000000 1.000000 1.000000");
                    mtlsw.WriteLine("Kd 0.640000 0.640000 0.640000");
                    mtlsw.WriteLine("map_Ka " + material.Value.Replace(" ", "") + ".png");
                    mtlsw.WriteLine("map_Kd " + material.Value.Replace(" ", "") + ".png");
                }

                mtlsw.Close();
            }

            exportworker.ReportProgress(85, "Exporting terrain geometry..");

            var indices = indicelist.ToArray();

            var adtname = Path.GetFileNameWithoutExtension(file);

            var objsw = new StreamWriter(Path.Combine(outdir, Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file).Replace(" ", "") + ".obj"));

            objsw.WriteLine("# Written by Marlamin's WoW OBJExporter. Original file: " + file);
            if (bakeQuality != "none")
            {
                objsw.WriteLine("mtllib " + Path.GetFileNameWithoutExtension(file).Replace(" ", "") + ".mtl");
            }

            var verticeCounter = 1;
            var chunkCounter = 1;
            foreach (var vertex in verticelist)
            {
                //objsw.WriteLine("# C" + chunkCounter + ".V" + verticeCounter);
                objsw.WriteLine("v " + vertex.Position.X.ToString("R") + " " + vertex.Position.Y.ToString("R") + " " + vertex.Position.Z.ToString("R"));
                objsw.WriteLine("vt " + vertex.TexCoord.X + " " + vertex.TexCoord.Y);
                objsw.WriteLine("vn " + vertex.Normal.X.ToString("R") + " " + vertex.Normal.Y.ToString("R") + " " + vertex.Normal.Z.ToString("R"));
                verticeCounter++;
                if (verticeCounter == 146)
                {
                    chunkCounter++;
                    verticeCounter = 1;
                }
            }

            if (bakeQuality == "minimap" || bakeQuality == "low" || bakeQuality == "medium")
            {
                objsw.WriteLine("g " + adtname.Replace(" ", ""));
                objsw.WriteLine("usemtl " + materials[1]);
                objsw.WriteLine("s 1");
            }

            for (int rbi = 0; rbi < renderBatches.Count(); rbi++)
            {
                var renderBatch = renderBatches[rbi];
                var i = renderBatch.firstFace;
                if (bakeQuality == "high" || bakeQuality == "none")
                {
                    objsw.WriteLine("g " + adtname.Replace(" ", "") + "_" + rbi);
                }

                if (bakeQuality == "high" && materials.ContainsKey((int)renderBatch.materialID))
                {
                    objsw.WriteLine("usemtl " + materials[(int)renderBatch.materialID]);
                }

                while (i < (renderBatch.firstFace + renderBatch.numFaces))
                {
                    objsw.WriteLine("f " +
                        (indices[i + 2] + 1) + "/" + (indices[i + 2] + 1) + "/" + (indices[i + 2] + 1) + " " +
                        (indices[i + 1] + 1) + "/" + (indices[i + 1] + 1) + "/" + (indices[i + 1] + 1) + " " +
                        (indices[i] + 1) + "/" + (indices[i] + 1) + "/" + (indices[i] + 1));
                    i = i + 3;
                }
            }

            objsw.Close();

            Logger.WriteLine("ADT OBJ Exporter: Finished with export of {0}..", file);
        }
    }
}
