﻿/*
Copyright (c) 2016 Radu

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

/*
DISCLAIMER
The reposiotory, code and tools provided herein are for educational purposes only.
The information not meant to change or impact the original code, product or service.
Use of this repository, code or tools does not exempt the user from any EULA, ToS or any other legal agreements that have been agreed with other parties.
The user of this repository, code and tools is responsible for his own actions.

Any forks, clones or copies of this repository are the responsability of their respective authors and users.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
//using System.Diagnostics; //remove this later

namespace AssetStudio
{
    public class AssetsFile
    {
        public EndianStream a_Stream;
        public string filePath;
        public int fileGen;
        public string m_Version = "2.5.0f5";
        public int[] version = new int[4] { 0, 0, 0, 0 };
        public string[] buildType;
        public int platform = 100663296;
        public string platformStr = "";
        //public EndianType endianType = EndianType.BigEndian;
        //public List<AssetPreloadData> preloadTable = new List<AssetPreloadData>();
        public Dictionary<long, AssetPreloadData> preloadTable = new Dictionary<long, AssetPreloadData>();
        public Dictionary<long, GameObject> GameObjectList = new Dictionary<long, GameObject>();
        public Dictionary<long, Transform> TransformList = new Dictionary<long, Transform>();

        public List<AssetPreloadData> exportableAssets = new List<AssetPreloadData>();
        public List<UnityShared> sharedAssetsList = new List<UnityShared>() {new UnityShared()};
        private ClassIDReference UnityClassID = new ClassIDReference();

        public SortedDictionary<int, ClassStrStruct> ClassStructures = new SortedDictionary<int, ClassStrStruct>();

        private bool baseDefinitions = false;

        public class UnityShared
        {
            public int Index = -1; //actual index in main list
            public string aName = "";
            public string fileName = "";
        }

        public AssetsFile(string fileName, EndianStream fileStream)
        {
            //if (memFile != null) { Stream = new EndianStream(memFile, endianType); }
            //else { Stream = new EndianStream(File.OpenRead(fileName), endianType); }
            a_Stream = fileStream;

            filePath = fileName;
            int tableSize = a_Stream.ReadInt32();
            int dataEnd = a_Stream.ReadInt32();
            fileGen = a_Stream.ReadInt32();
            int dataOffset = a_Stream.ReadInt32();
            sharedAssetsList[0].fileName = Path.GetFileName(fileName); //reference itself because sharedFileIDs start from 1

            switch (fileGen)
            {
                case 6://2.5.0 - 2.6.1
                    {
                        a_Stream.Position = (dataEnd - tableSize);
                        a_Stream.Position += 1;
                        break;
                    }
                case 7://3.0.0 beta
                    {
                        a_Stream.Position = (dataEnd - tableSize);
                        a_Stream.Position += 1;
                        m_Version = a_Stream.ReadStringToNull();
                        break;
                    }
                case 8://3.0.0 - 3.4.2
                    {
                        a_Stream.Position = (dataEnd - tableSize);
                        a_Stream.Position += 1;
                        m_Version = a_Stream.ReadStringToNull();
                        platform = a_Stream.ReadInt32();
                        break;
                    }
                case 9://3.5.0 - 4.6.x
                    {
                        a_Stream.Position += 4;//azero
                        m_Version = a_Stream.ReadStringToNull();
                        platform = a_Stream.ReadInt32();
                        break;
                    }
                case 14://5.0.0 beta and final
                case 15://5.0.1 and up
                    {
                        a_Stream.Position += 4;//azero
                        m_Version = a_Stream.ReadStringToNull();
                        platform = a_Stream.ReadInt32();
                        baseDefinitions = a_Stream.ReadBoolean();
                        break;
                    }
                default:
                    {
                        //MessageBox.Show("Unsupported version!", "AssetStudio Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
            }

            if (platform > 255 || platform < 0)
            {
                byte[] b32 = BitConverter.GetBytes(platform);
                Array.Reverse(b32);
                platform = BitConverter.ToInt32(b32, 0);
                //endianType = EndianType.LittleEndian;
                a_Stream.endian = EndianType.LittleEndian;
            }

            switch (platform)
            {
                case -2: platformStr = "Unt Package"; break;
                case 4: platformStr = "OSX"; break;
                case 5: platformStr = "PC"; break;
                case 6: platformStr = "Web"; break;
                case 7: platformStr = "Web streamed"; break;
                case 9: platformStr = "iOS"; break;
                case 10: platformStr = "PS3"; break;
                case 11: platformStr = "Xbox 360"; break;
                case 13: platformStr = "Android"; break;
                case 16: platformStr = "Google NaCl"; break;
                case 21: platformStr = "WP8"; break;
                case 25: platformStr = "Linux"; break;
            }
            
            int baseCount = a_Stream.ReadInt32();
            for (int i = 0; i < baseCount; i++)
            {
                if (fileGen < 14)
                {
                    int classID = a_Stream.ReadInt32();
                    string baseType = a_Stream.ReadStringToNull();
                    string baseName = a_Stream.ReadStringToNull();
                    a_Stream.Position += 20;
                    int memberCount = a_Stream.ReadInt32();

                    StringBuilder cb = new StringBuilder();
                    for (int m = 0; m < memberCount; m++) { readBase(cb, 1); }

                    var aClass = new ClassStrStruct() { ID = classID, Text = (baseType + " " + baseName), members = cb.ToString() };
                    aClass.SubItems.Add(classID.ToString());
                    ClassStructures.Add(classID, aClass);
                }
                else { readBase5(); }
            }

            if (fileGen >= 7 && fileGen < 14) {a_Stream.Position += 4;}//azero

            int assetCount = a_Stream.ReadInt32();

            #region asset preload table
            string assetIDfmt = "D" + assetCount.ToString().Length.ToString(); //format for unique ID

            for (int i = 0; i < assetCount; i++)
            {
                //each table entry is aligned individually, not the whole table
                if (fileGen >= 14) { a_Stream.AlignStream(4); }

                AssetPreloadData asset = new AssetPreloadData();
                if (fileGen < 14) { asset.m_PathID = a_Stream.ReadInt32(); }
                else { asset.m_PathID = a_Stream.ReadInt64(); }
                asset.Offset = a_Stream.ReadInt32();
                asset.Offset += dataOffset;
                asset.Size = a_Stream.ReadInt32();
                asset.Type1 = a_Stream.ReadInt32();
                asset.Type2 = a_Stream.ReadUInt16();
                a_Stream.Position += 2;
                if (fileGen >= 15)
                {
                    byte unknownByte = a_Stream.ReadByte();
                    //this is a single byte, not an int32
                    //the next entry is aligned after this
                    //but not the last!
                    if (unknownByte != 0)
                    {
                        bool investigate = true;
                    }
                }

                if (UnityClassID.Names[asset.Type2] != null)
                {
                    asset.TypeString = UnityClassID.Names[asset.Type2];
                }

                asset.uniqueID = i.ToString(assetIDfmt);
                
                asset.exportSize = asset.Size;
                asset.sourceFile = this;
                
                preloadTable.Add(asset.m_PathID, asset);

                #region read BuildSettings to get version for 2.x files
                if (asset.Type2 == 141 && fileGen == 6)
                {
                    long nextAsset = a_Stream.Position;

                    BuildSettings BSettings = new BuildSettings(asset);
                    m_Version = BSettings.m_Version;

                    a_Stream.Position = nextAsset;
                }
                #endregion
            }
            #endregion

            buildType = m_Version.Split(new string[] { ".", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" }, StringSplitOptions.RemoveEmptyEntries);
            string[] strver = (m_Version.Split(new string[] { ".", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "\n" }, StringSplitOptions.RemoveEmptyEntries));
            version = Array.ConvertAll(strver, int.Parse);

            if (fileGen >= 14)
            {
                //this looks like a list of assets that need to be preloaded in memory before anytihng else
                int someCount = a_Stream.ReadInt32();
                for (int i = 0; i < someCount; i++)
                {
                    int num1 = a_Stream.ReadInt32();
                    a_Stream.AlignStream(4);
                    long m_PathID = a_Stream.ReadInt64();
                }
            }

            int sharedFileCount = a_Stream.ReadInt32();
            for (int i = 0; i < sharedFileCount; i++)
            {
                UnityShared shared = new UnityShared();
                shared.aName = a_Stream.ReadStringToNull();
                a_Stream.Position += 20;
                string sharedFileName = a_Stream.ReadStringToNull(); //relative path
                shared.fileName = sharedFileName.Replace("/", "\\");
                sharedAssetsList.Add(shared);
            }
        }

        private void readBase(StringBuilder cb, int level)
        {
            string varType = a_Stream.ReadStringToNull();
            string varName = a_Stream.ReadStringToNull();
            //a_Stream.Position += 20;
            int size = a_Stream.ReadInt32();
            int index = a_Stream.ReadInt32();
            int isArray = a_Stream.ReadInt32();
            int num0 = a_Stream.ReadInt32();
            int num1 = a_Stream.ReadInt16();
            int num2 = a_Stream.ReadInt16();
            int childrenCount = a_Stream.ReadInt32();

            //Debug.WriteLine(baseFormat + " " + baseName + " " + childrenCount);
            cb.AppendFormat("{0}{1} {2} {3}\r\n", (new string('\t', level)), varType, varName, size);
            for (int i = 0; i < childrenCount; i++) { readBase(cb, level + 1); }
        }

        private void readBase5()
        {
            int classID = a_Stream.ReadInt32();
            if (classID < 0) { a_Stream.Position += 16; }
            a_Stream.Position += 16;

            if (baseDefinitions)
            {
                #region cmmon string array
                string[] baseStrings = new string[1007];
                baseStrings[0] = "AABB";
                baseStrings[5] = "AnimationClip";
                baseStrings[19] = "AnimationCurve";
                baseStrings[49] = "Array";
                baseStrings[55] = "Base";
                baseStrings[60] = "BitField";
                baseStrings[76] = "bool";
                baseStrings[81] = "char";
                baseStrings[86] = "ColorRGBA";
                baseStrings[106] = "data";
                baseStrings[138] = "FastPropertyName";
                baseStrings[155] = "first";
                baseStrings[161] = "float";
                baseStrings[167] = "Font";
                baseStrings[172] = "GameObject";
                baseStrings[183] = "Generic Mono";
                baseStrings[208] = "GUID";
                baseStrings[222] = "int";
                baseStrings[241] = "map";
                baseStrings[245] = "Matrix4x4f";
                baseStrings[262] = "NavMeshSettings";
                baseStrings[263] = "MonoBehaviour";
                baseStrings[277] = "MonoScript";
                baseStrings[299] = "m_Curve";
                baseStrings[349] = "m_Enabled";
                baseStrings[374] = "m_GameObject";
                baseStrings[427] = "m_Name";
                baseStrings[490] = "m_Script";
                baseStrings[519] = "m_Type";
                baseStrings[526] = "m_Version";
                baseStrings[543] = "pair";
                baseStrings[548] = "PPtr<Component>";
                baseStrings[564] = "PPtr<GameObject>";
                baseStrings[581] = "PPtr<Material>";
                baseStrings[616] = "PPtr<MonoScript>";
                baseStrings[633] = "PPtr<Object>";
                baseStrings[688] = "PPtr<Texture>";
                baseStrings[702] = "PPtr<Texture2D>";
                baseStrings[718] = "PPtr<Transform>";
                baseStrings[741] = "Quaternionf";
                baseStrings[753] = "Rectf";
                baseStrings[778] = "second";
                baseStrings[795] = "size";
                baseStrings[800] = "SInt16";
                baseStrings[814] = "int64";
                baseStrings[840] = "string";
                baseStrings[874] = "Texture2D";
                baseStrings[884] = "Transform";
                baseStrings[894] = "TypelessData";
                baseStrings[907] = "UInt16";
                baseStrings[928] = "UInt8";
                baseStrings[934] = "unsigned int";
                baseStrings[981] = "vector";
                baseStrings[988] = "Vector2f";
                baseStrings[997] = "Vector3f";
                baseStrings[1006] = "Vector4f";
                #endregion

                int varCount = a_Stream.ReadInt32();
                int stringSize = a_Stream.ReadInt32();

                a_Stream.Position += varCount * 24;
                string varStrings = Encoding.UTF8.GetString(a_Stream.ReadBytes(stringSize));
                string className = "";
                StringBuilder classVarStr = new StringBuilder();

                //build Class Structures
                a_Stream.Position -= varCount * 24 + stringSize;
                for (int i = 0; i < varCount; i++)
                {
                    ushort num0 = a_Stream.ReadUInt16();
                    byte level = a_Stream.ReadByte();
                    bool isArray = a_Stream.ReadBoolean();

                    ushort varTypeIndex = a_Stream.ReadUInt16();
                    ushort test = a_Stream.ReadUInt16();
                    string varTypeStr;
                    if (test == 0) //varType is an offset in the string block
                    { varTypeStr = varStrings.Substring(varTypeIndex, varStrings.IndexOf('\0', varTypeIndex) - varTypeIndex); }//substringToNull
                    else //varType is an index in an internal strig array
                    { varTypeStr = baseStrings[varTypeIndex] != null ? baseStrings[varTypeIndex] : varTypeIndex.ToString(); }

                    ushort varNameIndex = a_Stream.ReadUInt16();
                    test = a_Stream.ReadUInt16();
                    string varNameStr;
                    if (test == 0) { varNameStr = varStrings.Substring(varNameIndex, varStrings.IndexOf('\0', varNameIndex) - varNameIndex); }
                    else { varNameStr = baseStrings[varNameIndex] != null ? baseStrings[varNameIndex] : varNameIndex.ToString(); }

                    int size = a_Stream.ReadInt32();
                    int index = a_Stream.ReadInt32();
                    int num1 = a_Stream.ReadInt32();

                    if (index == 0) { className = varTypeStr + " " + varNameStr; }
                    else { classVarStr.AppendFormat("{0}{1} {2} {3}\r\n", (new string('\t', level)), varTypeStr, varNameStr, size); }

                    //for (int t = 0; t < level; t++) { Debug.Write("\t"); }
                    //Debug.WriteLine(varTypeStr + " " + varNameStr + " " + size);
                }
                a_Stream.Position += stringSize;

                var aClass = new ClassStrStruct() { ID = classID, Text = className, members = classVarStr.ToString() };
                aClass.SubItems.Add(classID.ToString());
                ClassStructures.Add(classID, aClass);
            }

        }

    }
}
