using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace R3EMemoryTranslator
{
    class Program
    {
        // fields to be mapped in the PlayerData struct
        static String[] PlayerDataMappings = { "PDGameSimulationTicks", "PDGameSimulationTime", "PDWorldPosition", "PDWorldVelocity", "PDWorldAcceleration", 
                                       "PDLocalAcceleration", "PDOrientation", "PDRotation", "PDAngularAcceleration" };
        // fields to be mapped in the root struct
        static String[] Mappings = { "EngineRps", "NumberOfLaps", "Position", "ControlType", "CarSpeed", "MaxEngineRps", "Gear", "CarCgLocation", 
                                   "CarOrientation", "LocalAcceleration", "FuelLeft", "FuelCapacity", "FuelPressure", "EngineOilPressure", "ThrottlePedal",
                                   "BrakePedal", "ClutchPedal", "BrakeTemp", "TireTemp", "LapTimePreviousSelf", "LapTimeCurrentSelf", "LapTimeBestSelf", 
                                   "TimeDeltaFront", "TimeDeltaBehind", "CutTrackWarnings" };

        // Byte offsets of fields we're going to map, initialised when we start the app
        static Dictionary<string, int> newStructPositions = new Dictionary<string, int>();
        static Dictionary<string, int> oldStructPositions = new Dictionary<string, int>();
        static Dictionary<string, int> newStructPDPositions = new Dictionary<string, int>();
        static Dictionary<string, int> oldStructPDPositions = new Dictionary<string, int>();

        // default field length is 4, unless it appears here:
        // TODO: get field lengths by reflection (this is harder than is should be)
        static Dictionary<string, int> fieldLengths = new Dictionary<string, int>();

        // the new MMF (source data)
        static MemoryMappedViewAccessor _inputView;
        // the old MMF (destination data)
        static MemoryMappedViewAccessor _outputView;

        static void Main(string[] args)
        {
            Console.WriteLine("Waiting for input data");
            initFieldLengths();
            createStructPositionMappings();
            while (!getInputMMF())
            {
                Thread.Sleep(2000);
            }
            Console.WriteLine("Got input data");
            MemoryMappedFile outputMMF = MemoryMappedFile.CreateOrOpen(OldStruct.SharedMemoryName, 4096);
            _outputView = outputMMF.CreateViewAccessor();
            Console.WriteLine("Created output data, mapping...");
            DateTime start = DateTime.Now;
            int count = 0;
            while (true)
            {
                copyData();
                Thread.Sleep(10);
                count++;
                if (count % 1000 == 0)
                {
                    Console.WriteLine("Conversion rate = " + 1000 / ((DateTime.Now - start).TotalSeconds) + "Hz");
                    start = DateTime.Now;
                }
            }
        }

        // all field lengths are 4 bytes unless stated here. 
        // These are hard-coded because I'm too stupid / lazy / awesome to get them by reflection
        static void initFieldLengths() {
            fieldLengths.Add("PDGameSimulationTime", 8); 
            fieldLengths.Add("PDWorldPosition", 24); 
            fieldLengths.Add("PDWorldVelocity", 24); 
            fieldLengths.Add("PDWorldAcceleration", 24); 
            fieldLengths.Add("PDLocalAcceleration", 24); 
            fieldLengths.Add("PDOrientation", 24); 
            fieldLengths.Add("PDRotation", 24); 
            fieldLengths.Add("PDAngularAcceleration", 24); 

            fieldLengths.Add("CarCgLocation", 12); 
            fieldLengths.Add("LocalAcceleration", 12); 
            fieldLengths.Add("BrakeTemp", 12); 
            fieldLengths.Add("TireTemp", 48);
        }

        static void createStructPositionMappings()
        {
            int newPDPosition = GetNewFieldOffset("Player");
            int oldPDPosition = GetOldFieldOffset("Player");
            foreach (String name in PlayerDataMappings)
            {
                newStructPDPositions.Add(name, GetNewPlayerDataFieldOffset(name) + newPDPosition);
                oldStructPDPositions.Add(name, GetOldPlayerDataFieldOffset(name) + oldPDPosition);
            }
            foreach (String name in Mappings)
            {
                newStructPositions.Add(name, GetNewFieldOffset(name));
                oldStructPositions.Add(name, GetOldFieldOffset(name));
            }
        }

        static Boolean getInputMMF() {
            Boolean gotInputMMF = false;
            try 
            {
                MemoryMappedFile inputMMF = MemoryMappedFile.OpenExisting(NewStruct.SharedMemoryName);
                _inputView = inputMMF.CreateViewAccessor();
                gotInputMMF = true;
            }
            catch (FileNotFoundException e)
            {
                // R3E not running?
            }
            return gotInputMMF;
        }

        /**
         * Copy all the mapped data from the source struct to the destination struct          * 
         */
        static void copyData()
        {
            foreach (String mapping in Mappings)
            {
                WriteBytes(oldStructPositions[mapping], ReadBytes(newStructPositions[mapping], getFieldLength(mapping)));
            }
            foreach (String mapping in PlayerDataMappings)
            {
                WriteBytes(oldStructPDPositions[mapping], ReadBytes(newStructPDPositions[mapping], getFieldLength(mapping)));
            }
        }

        /**
         * Quick-n-dirty MMF read
         */
        static unsafe byte[] ReadBytes(int offset, int num)
        {
            byte[] arr = new byte[num];
            byte* ptr = (byte*)0;
            _inputView.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(new IntPtr(ptr), offset), arr, 0, num);
            _inputView.SafeMemoryMappedViewHandle.ReleasePointer();
            return arr;
        }

        /**
         * Quick-n-dirty MMF write
         */
        static unsafe void WriteBytes(int offset, byte[] data)
        {
            byte* ptr = (byte*)0;
            _outputView.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            System.Runtime.InteropServices.Marshal.Copy(data, 0, IntPtr.Add(new IntPtr(ptr), offset), data.Length);
            _outputView.SafeMemoryMappedViewHandle.ReleasePointer();
        }

        /**
         * Field length is always 4 bytes unless explicitly set.
         */
        static int getFieldLength(String fieldName)
        {
            if (fieldLengths.ContainsKey(fieldName))
            {
                return fieldLengths[fieldName];
            }
            else
            {
                return 4;
            }
        }

        static int GetNewFieldOffset(string fieldName)
        {
            return Marshal.OffsetOf(typeof(NewStruct.RaceRoomShared), fieldName).ToInt32();
        }

        static int GetNewPlayerDataFieldOffset(string fieldName)
        {
            return Marshal.OffsetOf(typeof(NewStruct.PlayerData), fieldName).ToInt32();  
        }

        static int GetOldFieldOffset(string fieldName)
        {
            return Marshal.OffsetOf(typeof(OldStruct.RaceRoomShared), fieldName).ToInt32();  
        }

        static int GetOldPlayerDataFieldOffset(string fieldName)
        {
            return Marshal.OffsetOf(typeof(OldStruct.PlayerData), fieldName).ToInt32();  
        }

        // TODO: offsets of fields in other nested structures.
    }
}
