/*
 * Wait buffer data from pipe or TCP(9001)
 * Store and provide images
 * 
 * Command (INT8)
 *  Key(BYTE),   HWND(INT64),  IMAGE (BYTE),  
 * 
 * Exist        HWND        CMD,HWND,True/False
 *              KEY         
 *              
 * Store        HWND        CMD,HWND,MODE,IDX,REFRESH  (refresh=  kell e a kliensnek adatot kérni az imagebuffertől az adott HWND-re?
 *              Image       
 * 
 * Retrieve     HWND        CMD,HWND,IMAGE           //legfrissebb
 *              
 *              
 */




using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ImageBuffer
{
    class Program
    {
        static string shipUUID = "52f1b4e2-61e2-4ca0-8d21-e70b509e7693";  //This Pod is a SHIP
        const bool DEBUG_OnlyWin = false;

        public static int port_imagebuffer = 9001;
        public static string ip_imagebuffer = "127.0.0.1";

        static MediaServer mediaserver;
        static ObjectIDGenerator IDGenerator;
        static ClientFollower clientFollower;

        static BabylonMS.BabylonMS tcp;
        static void Main(string[] args)
        {
            mediaserver = new MediaServer();
            IDGenerator = new ObjectIDGenerator();
            clientFollower = new ClientFollower();

            tcp = BabylonMS.BabylonMS.ShipDocking(ip_imagebuffer, port_imagebuffer, shipUUID);
            tcp.Connected += TCPClientConnected;
            tcp.Disconnected += TCPDisconnected;
            tcp.NewInputFrame += TCPNewInputFrame;
            tcp.OpenGate(true);//NET Server

        }

        static void TCPClientConnected(BabylonMS.BMSEventSessionParameter session)
        {
            //Console.WriteLine("TCP Client Connected");
        }

        static void TCPDisconnected(BabylonMS.BMSEventSessionParameter session)
        {
            //Console.WriteLine("TCP Server Disconnected");
        }

        static void TCPNewInputFrame(BabylonMS.BMSEventSessionParameter session)
        {
            InputFrame(tcp,session); 
        }

        static Semaphore sema1 = new Semaphore(1, 1);
        static void InputFrame(BabylonMS.BabylonMS bms, BabylonMS.BMSEventSessionParameter session) //newinput frame or continuous 
        {
            String partnerUUID = session.shipUUID;


            bool isFirstTime;
            Int64 ID = IDGenerator.GetId(session.reader, out isFirstTime); //The client ID from reader instance unique ID

            try
            {
                /*
                //Az inputpack paramétermintázatának ellenőrzésére szolgál (nem szükséges)
                //Elválasztva egymás után a paramétermintázatok. 
                session.inputPack.AcceptedEnergyPattern(new byte[] {
                    BabylonMS.BabylonMS.CONST_FT_INT8,BabylonMS.BabylonMS.CONST_FT_INT64,BabylonMS.BabylonMS.CONST_FT_BYTE,BabylonMS.BabylonMS.CONST_FT_END,
                    BabylonMS.BabylonMS.CONST_FT_INT8,BabylonMS.BabylonMS.CONST_FT_INT64,BabylonMS.BabylonMS.CONST_FT_END
                }
                );
                */

                byte command = (byte)session.inputPack.GetField(0).getValue(0);
                BabylonMS.BMSPack outputpack = new BabylonMS.BMSPack();
                Int64 hwnd;
                String group;
                String key;
                MediaBuffer buf;
                //String owner = partnerUUID;
                switch (command)
                {
                    case VRMainContentExporter.VRCEShared.CONST_COMMAND_GETBUFFER:
                        //TODO : NOT TESTED
                        outputpack.AddField("CMD", BabylonMS.BabylonMS.CONST_FT_INT8).Value(command);
                        //bool first = true;                        
                        BabylonMS.BMSField f1 = null;
                        BabylonMS.BMSField f2 = null;
                        BabylonMS.BMSField f3 = null;
                        BabylonMS.BMSField f4 = null;
                        BabylonMS.BMSField f5 = null;
                        int cnt = mediaserver.SIBuffer.Count();
                        byte idx=0;
                        for ( ; idx<cnt; idx++)
                        {
                            buf = mediaserver.SIBuffer[idx];
                            if (idx==0) {
                                f1 = outputpack.AddField("IDX", BabylonMS.BabylonMS.CONST_FT_INT8);
                                f2 = outputpack.AddField("HWND", BabylonMS.BabylonMS.CONST_FT_INT64);
                                f3 = outputpack.AddField("GROUP", BabylonMS.BabylonMS.CONST_FT_UUID); //UUID length string array
                                f4 = outputpack.AddField("DATE", BabylonMS.BabylonMS.CONST_FT_INT64);
                                f5 = outputpack.AddField("USAGE", BabylonMS.BabylonMS.CONST_FT_INT32); 
                            }
                            f1.Value(idx);
                            f2.Value(buf.hwnd);
                            f3.ValueAsUUID(buf.owner); //mivel owner = UUID
                            f4.Value(buf.created.Ticks);
                            f5.Value(buf.usage);
                        }
                        if (idx>0)
                            bms.TransferPacket(session.writer, outputpack, true);
                        break;
                    case VRMainContentExporter.VRCEShared.CONST_COMMAND_EXIST:
                        //TODO : NOT TESTED
                        hwnd = session.inputPack.GetFieldByName("HWND").getValue(0);
                        group = session.inputPack.GetFieldByName("GROUP").GetUUIDValue(0);
                        key = System.Text.Encoding.ASCII.GetString(session.inputPack.GetField(2).getValue());
                        outputpack.AddField("CMD"   , BabylonMS.BabylonMS.CONST_FT_INT8 ).Value(command);
                        outputpack.AddField("HWND", BabylonMS.BabylonMS.CONST_FT_INT64).Value(hwnd);
                        outputpack.AddField("GROUP", BabylonMS.BabylonMS.CONST_FT_UUID).ValueAsUUID(group);
                        sema1.WaitOne();
                        outputpack.AddField("EXISTS", BabylonMS.BabylonMS.CONST_FT_INT8 ).Value(mediaserver.Exists(group, key, hwnd));
                        sema1.Release();
                        bms.TransferPacket(session.writer,outputpack, true);
                        break;
                    case VRMainContentExporter.VRCEShared.CONST_COMMAND_STORE:
                        hwnd = session.inputPack.GetFieldByName("HWND").getValue(0);
                        group = session.inputPack.GetFieldByName("GROUP").GetUUIDValue(0);
                        byte[] image = session.inputPack.GetFieldByName("IMAGE").getValue();
                        sema1.WaitOne();
                        byte[] res = mediaserver.indexOfBufferAndStore(group, MediaServer.md5(image), image, hwnd);
                        bool refresh = clientFollower.IsNeedRefresh(ID,res[0],res[1]);
                        sema1.Release();                        
                        outputpack.AddField("CMD"    , BabylonMS.BabylonMS.CONST_FT_INT8 ).Value(command);
                        outputpack.AddField("HWND", BabylonMS.BabylonMS.CONST_FT_INT64).Value(hwnd);
                        outputpack.AddField("GROUP", BabylonMS.BabylonMS.CONST_FT_UUID).ValueAsUUID(group);
                        outputpack.AddField("MODE" ,   BabylonMS.BabylonMS.CONST_FT_INT8 ).Value(res[0]);
                        outputpack.AddField("IDX",   BabylonMS.BabylonMS.CONST_FT_INT8 ).Value(res[1]);
                        outputpack.AddField("REFRESH", BabylonMS.BabylonMS.CONST_FT_INT8 ).Value(refresh);
                        if ((refresh) && (res[0]!= VRMainContentExporter.VRCEShared.CONST_MODE_BFFOUND)) { 
                            MediaBuffer buf2 = mediaserver.Retrieve(res[1]);
                            if (buf2!=null)
                                outputpack.AddField("IMAGE", BabylonMS.BabylonMS.CONST_FT_BYTE).Value(buf2.buffer);
                        }
                        bms.TransferPacket(session.writer,outputpack,true);
                        //Console.WriteLine();
                        break;
                    case VRMainContentExporter.VRCEShared.CONST_COMMAND_RETRIEVE:
                        outputpack.AddField("CMD", BabylonMS.BabylonMS.CONST_FT_INT8).Value(command);
                        Int64 androidReqID = session.inputPack.GetFieldByName("REQID").getValue(0);
                        outputpack.AddField("REQID", BabylonMS.BabylonMS.CONST_FT_INT64).Value(androidReqID);
                        if (session.inputPack.FieldsCount() > 2)
                        {
                            //TODO mert nem ellenőriztem ;s mert lehet hogy a UUID+HWND parost kellene lek;rdezni!!!!
                            for (int i = 2; i < session.inputPack.FieldsCount(); i+=2) //CMD in first position
                            {
                                hwnd = session.inputPack.GetField(i).getValue(0);
                                group = session.inputPack.GetField(i+1).GetUUIDValue(0);
                                sema1.WaitOne();
                                buf = mediaserver.Retrieve(group, hwnd);
                                outputpack.AddField("IDX",   BabylonMS.BabylonMS.CONST_FT_INT8 ).Value((byte)buf.position_in_buffer);
                                outputpack.AddField("HWND",  BabylonMS.BabylonMS.CONST_FT_INT64).Value(hwnd);
                                outputpack.AddField("GROUP",  BabylonMS.BabylonMS.CONST_FT_UUID).ValueAsUUID(group);
                                outputpack.AddField("IMAGE", BabylonMS.BabylonMS.CONST_FT_BYTE ).Value(buf.buffer);
                                sema1.Release();
                            }
                        } else
                        {
                            //All uptodate element from buffer because no specified sent HWND
                            List<string> owners = mediaserver.GetOwners();
                            foreach (var o in owners) {
                                List<Int64> hwnds = mediaserver.GetOwnerHwnds(o);
                                foreach (var h in hwnds)
                                {
                                    sema1.WaitOne();
                                    buf = mediaserver.Retrieve(o, h);
                                    if (buf != null)
                                    {
                                        outputpack.AddField("IDX", BabylonMS.BabylonMS.CONST_FT_INT8).Value((byte)buf.position_in_buffer);
                                        BabylonMS.BMSField fi = outputpack.AddField("HWND", BabylonMS.BabylonMS.CONST_FT_INT64);
                                        fi.Value(h);
                                        outputpack.AddField("GROUP", BabylonMS.BabylonMS.CONST_FT_UUID).ValueAsUUID(o);
                                        outputpack.AddField("IMAGE", BabylonMS.BabylonMS.CONST_FT_BYTE).Value(buf.buffer);
                                    }
                                    sema1.Release();
                                }
                            }
                        }
                        bms.TransferPacket(session.writer,outputpack,true);
                        break;
                }
            }
            catch (Exception ) {

            };
        }

        static void ClientConnected(StreamReader reader, StreamWriter writer)
        {

        }

    }

    //Connected client specific things
    class ClientFollower 
    {
        List<ClientFollowerData> data;

        public ClientFollower()
        {
            data = new List<ClientFollowerData>();
        }

        //jelzés a kliensnek, hogy kell e frissítenie a képernyőképet , változott e valami
        //sok thread egyenként a StreamReader ID-vel azonosítva
        public bool IsNeedRefresh(Int64 instanceID, byte mode, byte index)
        {
            bool res = true;
            ClientFollowerData dt = data.Find(
                delegate (ClientFollowerData data)
                {
                    return (data.ID == instanceID);
                }
            );
            if (dt != null)
            {
                if ((mode== VRMainContentExporter.VRCEShared.CONST_MODE_BFFOUND) &&(dt.mode == mode) && (dt.index == index)) { return false; }
                if ((mode == VRMainContentExporter.VRCEShared.CONST_MODE_BFADD) || (mode == VRMainContentExporter.VRCEShared.CONST_MODE_BFMODIFY))
                {
                    res = true;
                }
                else
                {
                    if (index == dt.index)
                        res = false;
                    else
                        res = true;                    
                }
                data.Remove(dt);
                dt = new ClientFollowerData(instanceID, mode, index);
                data.Add(dt);

                return res;
            }
            dt = new ClientFollowerData(instanceID, mode, index);
            data.Add(dt);

            return true;
        }
    }
    class ClientFollowerData
    {
        public Int64 ID;
        public byte mode;
        public byte index;

        public ClientFollowerData(long iD, byte mode, byte index)
        {
            ID = iD;
            this.mode = mode;
            this.index = index;
        }
    }


    class MediaServer
    {
        public List<MediaBuffer> SIBuffer;
        public List<HwndBuffer> hwndbuffer;

        const byte CONST_MAXBUFF = 100;
        public int SIX = 0;

        public MediaServer()
        {
            SIBuffer = new List<MediaBuffer>();
            hwndbuffer = new List<HwndBuffer>();
        }

        public static string md5(string value)
        {
            var provider = MD5.Create();
            byte[] bytes = provider.ComputeHash(Encoding.ASCII.GetBytes(value));
            string computedHash = BitConverter.ToString(bytes);
            return computedHash.Replace("-", "");
        }
        public static string md5(byte[] value)
        {
            var provider = MD5.Create();
            byte[] bytes = provider.ComputeHash(value);
            string computedHash = BitConverter.ToString(bytes);
            return computedHash.Replace("-", "");
        }
        public static string md5(MemoryStream mem)
        {
            var provider = MD5.Create();
            mem.Seek(0, SeekOrigin.Begin);
            byte[] bytes = provider.ComputeHash(mem);
            return BitConverter.ToString(bytes);
        }


        public bool Exists(string Owner, string hash, Int64 Hwnd)
        {
            return Retrieve(Owner, hash, Hwnd) != null;
        }

        Semaphore store = new Semaphore(1, 1);
        public MediaBuffer Retrieve(string Owner, string hash, Int64 Hwnd)
        {
            store.WaitOne();
            foreach (MediaBuffer buf in SIBuffer)
            {
                if (buf.owner.CompareTo(Owner) == 0)
                {
                    if (buf.key.CompareTo(hash) == 0)
                    {
                        store.Release();
                        return buf;
                    }
                }
            }
            store.Release();
            return null;
        }
        public MediaBuffer Retrieve(int index)
        {
            if ((index < 0) || (index >= SIBuffer.Count)) return null;
            return SIBuffer[index];
        }
        //UptoDate 
        public MediaBuffer Retrieve(string Owner, Int64 Hwnd)
        {
            MediaBuffer uptodatebuf=null;
            DateTime newest = DateTime.MinValue;
            //int i = 0;
            int index = -1;
            MediaBuffer buf;
            store.WaitOne();
            for(int i=0; i<SIBuffer.Count(); i++ )
            //foreach (MediaBuffer buf in SIBuffer)
            {
                buf = SIBuffer[i];
                if (buf.owner.CompareTo(Owner) == 0)
                {
                    if (buf.hwnd == Hwnd)
                    {
                        if (buf.created > newest)
                        {
                            uptodatebuf = buf;
                            newest = buf.created;
                            index = i;
                        }
                    }
                }                
            }
            store.Release();
            uptodatebuf.position_in_buffer = index;
            return uptodatebuf;
        }

        //get All OwnerBuffer with most uptodate hwnds 
        public List<String> GetOwners()
        {
            List<String> res = new List<string>();

            foreach (MediaBuffer buf in SIBuffer)
            {
                if (res.FindIndex(x => (x.CompareTo(buf.owner)==0))==-1  )
                {
                    res.Add(buf.owner);
                } 
            }
            return res;
        }
        public List<Int64> GetOwnerHwnds(String Owner)
        {
            List<Int64> res = new List<Int64>();
            foreach (MediaBuffer buf in SIBuffer)
            {
                if (buf.owner.CompareTo(Owner) == 0)
                {
                    if (res.FindIndex(x => (x == buf.hwnd)) == -1)
                    {
                        res.Add(buf.hwnd);
                    }
                }
            }
            return res;
        }


        // return kétbyte-os tömb első bájt a mód, második a letárolási index
        public byte[] indexOfBufferAndStore(string Owner,string hash, byte[] mem, Int64 Hwnd)
        {            
            byte[] ret = new byte[2];
            MediaBuffer buf = null, old = null;
            byte oldi = 0;
            int index = -1;
            DateTime olddt = DateTime.Now;
            store.WaitOne();
            byte cnt = (byte)SIBuffer.Count();
            for (byte i = 0; i < cnt; i++)
            {
                buf = SIBuffer[i];
                if (buf.owner.CompareTo(Owner) == 0) {
                    if (buf.hwnd.CompareTo(Hwnd) == 0) {
                        index = i; }                            //Talált adatot az adott HWND-hez, azaz olyat ami nem egyezik de ehhet az ablakhoz köthető korábbi
                    if (buf.key.CompareTo(hash) == 0) {
                        ret[0] = VRMainContentExporter.VRCEShared.CONST_MODE_BFFOUND; ret[1] = i;
                        buf.created = DateTime.Now;             //még ha nem is kell tenni semmit sem.. az uptodate ez a frame lesz. Lekérdezés ez alapján működik legfrissebb = aktuális frame
                        buf.usage++;
                        HwndBuffer.APPStep(Hwnd, hwndbuffer);
                        store.Release();
                        return ret;                             //Megtalálta a megfelelő buffer elemet, sem hozzádás, sem módosítás nem történt
                    }
                }
                if ((olddt > buf.created) || (buf.del))         //TODO BUF.DEL előkészülve ha lesz process ami kitölti...
                {   olddt = buf.created;old = buf;oldi = i;     //A legrégebbi buffer elem
                }
            }
            //új felvétele - ha idáig eljutottunk, akkor nem találtuk meg

            int freq = HwndBuffer.APPFrequency(Hwnd, hwndbuffer);           // 1 érték azt jelenti minden alkalommal ha HWND adat jön az új, 2 azt jelenti minden második alkalommal új...
            if ((index < 0) || (cnt < 1) || (freq > 1))         // cnt=0 akkor ADD lesz (azaz üres a buffer), 
                                                                // index = vagy ha nincs a bufferben az ablakhoz régi elem. akkor sem lehet arra ráülni újra
                                                                // 
            {
                if (cnt >= CONST_MAXBUFF)
                {                                               // ha tele a buffer akkor a legrégebbi törlése 
                    MediaBuffer.Modify(old, Owner, hash, mem, Hwnd);
                    ret[0] = VRMainContentExporter.VRCEShared.CONST_MODE_BFMODIFY; ret[1] = oldi;           // A legrégebbi buffer elemre hivatkozás módosításra
                    store.Release();
                    return ret;
                }
                else
                {
                    SIBuffer.Add(new MediaBuffer(Owner, hash, mem, Hwnd));
                    SIX++;
                    ret[0] = VRMainContentExporter.VRCEShared.CONST_MODE_BFADD; ret[1] = (byte)(SIX - 1);   // Van még hely új elem részére felvesszük
                    store.Release();
                    return ret;
                }
            } else
            {                                                   // A bufferben vannak elemek már, 
                                                                // az adott ablakhoz tartozik már elem
                                                                // Az ismétlési freq (1) alacsony, azaz minden frame más és más és ezzel nem terheljük a buffert
                                                                // módosítjuk a gyorsan változó tartalmú ablak adatát a bufferben
                MediaBuffer.Modify(SIBuffer[index], Owner, hash, mem, Hwnd);
                ret[0] = VRMainContentExporter.VRCEShared.CONST_MODE_BFMODIFY; ret[1] = (byte)index;
                store.Release();
                return ret;
            }
        }
    }

    class HwndBuffer
    {
        int counter;
        Int64 Hwnd;
        public HwndBuffer(Int64 Hwnd, List<HwndBuffer> hwndbuffer)
        {
            this.Hwnd = Hwnd;
            counter = 1;
            hwndbuffer.Add(this);
        }
        public int Increment()
        {
            if ((++counter) > 10) counter = 10;
            return counter;
        }
        public int Value()
        {
            if ((--counter)<1 ) counter = 1;
            return counter;
        }
        public static HwndBuffer Search(Int64 Hwnd, List<HwndBuffer> hwndbuffer)
        {
            foreach(HwndBuffer b in hwndbuffer)
            {
                if (b.Hwnd == Hwnd) return b;
            }
            return null;
        }
        public static int APPFrequency(Int64 Hwnd, List<HwndBuffer> hwndbuffer)
        {
            HwndBuffer b=Search(Hwnd,hwndbuffer);
            if (b != null)
            {
                return b.Value();
            } else
            {                                   //még nincs a bufferben tároljuk el
                b = new HwndBuffer(Hwnd,hwndbuffer);
                return b.counter;
            }
        }
        public static int APPStep(Int64 Hwnd, List<HwndBuffer> hwndbuffer)   //amikor találunk korábbi eltárolt buffer adatot
        {
            HwndBuffer b = Search(Hwnd,hwndbuffer);
            if (b != null)
            {
                return b.Increment();                
            }
            else
            {                                   //még nincs a bufferben tároljuk el
                b = new HwndBuffer(Hwnd,hwndbuffer);
                return b.counter;
            }
        }
    }

    class MediaBuffer
    {
        public string owner;
        public string key;          //hash
        public byte[] buffer;
        public bool del = false;    // törölhető-e? a törölhetők kerülnek először törlésre, ha a buffer betelik
        public DateTime created;    //a legrégebbi kerül törlésre amikor a buffer betelik és nincs már DEL
        public int usage;           // ha add, vagy modify akkor nem volt használva , azaz ++ ha nincs változás csak használat
        public Int64 hwnd;
        public int position_in_buffer = -1; //only use when retrieve and set in retrieve so temp variable

        public MediaBuffer(string Owner,string Key, byte[] mem, Int64 Hwnd)
        {
            usage = 0;
            owner = Owner;
            buffer = mem;
            key = Key;
            created = DateTime.Now;
            hwnd = Hwnd;            
        }

        public static void Modify(MediaBuffer buf, string Owner,string Key, byte[] mem, Int64 Hwnd)
        {
            if (buf != null)
            {
                buf.usage = 0;
                buf.owner = Owner;
                buf.buffer = mem;
                buf.key = Key;
                buf.created = DateTime.Now;
                buf.hwnd = Hwnd;
            }
        }
    }
}
