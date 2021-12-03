using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Game;
using GameFramework;
using GameFramework.Network;
using ProtoBuf;
using ProtoBuf.Meta;
using UnityEngine;
using UnityGameFramework.Runtime;
using AddressFamily = System.Net.Sockets.AddressFamily;
using GameEntry = Game.GameEntry;

/// <summary>
/// 服务器 --TODO 只是写在 unity里面 模拟服务器的 收发
/// </summary>
public partial class ServerComponent : GameFrameworkComponent
{
     //客户端To服务器的包
     private readonly Dictionary<int, Type> m_ClientToServerPacketTypes = new Dictionary<int, Type>();
     private readonly PackDispatche m_PackDispatche = new PackDispatche();

     private Socket m_ServerSocket;    //测试服务器
     private Socket m_ClientSocket;    //连接的客户端

     private Thread m_ReceiveTherad;   //接收客户端的线程
     private byte[] m_ReceiveBuffer;   //接收数据包的字节缓冲数据流
     
     private MemoryStream m_ReceiveState;                              //接收状态流
     private MemoryStream m_SendState;                                 //发送状态流
     
     private readonly Queue<byte[]> m_SendQuene = new Queue<byte[]>(); //发送消息队列
     private Action m_CheckSendQuene;                                  //核查队列的委托

     //包头的长度 现在是8
     private int PacketHeaderLength
     {
          get
          {
               return sizeof(int) * 2;
          }
     }
     
     protected override void Awake()
     {
          base.Awake();
          
          m_ReceiveBuffer = new byte[1024 * 10];
          m_ReceiveState = new MemoryStream();
          m_SendState = new MemoryStream(1024 * 10);
          Init("127.0.0.1", 17779);
          m_CheckSendQuene = OnCheckSendQuene;
     }

     private void Start()
     {
          Type packetBaseType = typeof(CSPacketBase);      //客户端包-服务器包
          Type packetHandlerBaseType = typeof(PacketHandlerBase);   //包处理者
          Assembly assembly = Assembly.GetExecutingAssembly();
          Type[] types = assembly.GetTypes();
          for (int i = 0; i < types.Length; i++)
          {
               if (!types[i].IsClass || types[i].IsAbstract)
               {
                    continue;
               }
                
               if (types[i].BaseType == packetBaseType)
               {
                    PacketBase packetBase = (PacketBase)Activator.CreateInstance(types[i]);
                    Type packetType = GetClientToServerPacketType(packetBase.Id);
                    if (packetType != null)
                    {
                         Log.Warning("Already exist packet type '{0}', check '{1}' or '{2}'?.", packetBase.Id.ToString(), packetType.Name, packetBase.GetType().Name);
                         continue;
                    }

                    //客户端-服务器的消息包注册进去
                    m_ClientToServerPacketTypes.Add(packetBase.Id, types[i]);
               }
               else if (types[i].BaseType == packetHandlerBaseType) //处理者
               {
                    IPacketHandler packetHandler = (IPacketHandler)Activator.CreateInstance(types[i]);
                    
                    //关键一步 注册处理消息分发
                    m_PackDispatche.Subscribe(packetHandler.Id, packetHandler.Handle);
               }
          }
     }

     private void OnDestroy()
     {
          if (m_ClientSocket != null)
          {
               m_ClientSocket.Shutdown(SocketShutdown.Both);
               m_ClientSocket.Close();
          }
          
          m_ServerSocket.Close();
          m_ReceiveTherad.Abort();
     }

     private void Init(string ip,int port)
     {
          Log.Info("Open Server");

          m_ServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
          m_ServerSocket.Bind(new IPEndPoint(IPAddress.Parse(ip), port));
          m_ServerSocket.Listen(1);
          
          m_ReceiveTherad = new Thread(ListenClientCallBack);
          m_ReceiveTherad.IsBackground = true;
          m_ReceiveTherad.Start();
     }
     
     private void ListenClientCallBack()
     {
          while (true)
          {
               m_ClientSocket = m_ServerSocket.Accept();
               Log.Info($"服务器:{m_ClientSocket.RemoteEndPoint}已经连接");

               Thread thread = new Thread(ReceiveMessage);
               thread.IsBackground = true;
               thread.Start();
          }
          // ReSharper disable once FunctionNeverReturns
     }
     
     private void ReceiveMessage()
     {
          m_ClientSocket.BeginReceive(m_ReceiveBuffer, 0, m_ReceiveBuffer.Length, SocketFlags.None, OnReceiveCallback, m_ClientSocket);
     }

     private void OnReceiveCallback(IAsyncResult ar)
     {
          Socket socket = (Socket)ar.AsyncState;
          try
          {
               int bytesReceived = socket.EndReceive(ar);

               if (bytesReceived > 0)
               {
                    //把接收到的数据 写入缓冲数据流的尾部
                    m_ReceiveState.Position = m_ReceiveState.Length;

                    m_ReceiveState.Write(m_ReceiveBuffer, 0, bytesReceived);

                    //如果缓存数据流的长度大于包头的长度  至少有一个不完整的包过来了
                    if (m_ReceiveState.Length > PacketHeaderLength)
                    {
                         while (true)
                         {
                              m_ReceiveState.Position = 0;
                              CSPacketHeader header = Serializer.DeserializeWithLengthPrefix<CSPacketHeader>(m_ReceiveState, PrefixStyle.Fixed32);
                              if (header == null)
                              {
                                   Log.Error("包头解析失败");
                              }
                              else
                              {
                                   int packetBodyLen = header.PacketLength; //包体长度
                                   int fullPacketLen = packetBodyLen + PacketHeaderLength;   //整包长度

                                   //有一个完整包
                                   if (m_ReceiveState.Length >= fullPacketLen)
                                   {
                                        //容纳包体的byte[] 缓存区
                                        byte[] packetBodyBuffer = new byte[packetBodyLen];

                                        //把包的指向移动至包体处
                                        m_ReceiveState.Position = PacketHeaderLength;
                                        
                                        //读取全部的包体数据至缓存区
                                        m_ReceiveState.Read(packetBodyBuffer, 0, packetBodyLen);

                                        //包头的id 就是包体的协议号
                                        int packetCode = header.Id;
                                        
                                        //获取客户端到服务器的包类型
                                        Type packType = GetClientToServerPacketType(packetCode); 
                                        
                                        //通过协议号 得到包体的类型
                                        Packet packet = (Packet)RuntimeTypeModel.Default.DeserializeWithLengthPrefix(m_ReceiveState, ReferencePool.Acquire(packType), packType, PrefixStyle.Base128,0);

                                        if (packet == null)
                                        {
                                             Log.Error($"包体{packetCode}解析失败");
                                        }
                                        else
                                        {
                                             Debug.Log($"服务器:分发{packet.Id}消息");
                                             //通过 处理者分发消息
                                             m_PackDispatche.Fire(this,packet);   //GameEntry.Event.Fire(this,packet); GF案例 我们因为得不到 EventPool 临时写一个处理者
                                        }

                                        //-----------------------------------------------------
                                        //处理包太长了 不止一个包的情况
                                        //-----------------------------------------------------

                                        //流的长度 - 全包长 = 剩余字节了
                                        int remainLen = (int)m_ReceiveState.Length - fullPacketLen;
                                        
                                        if (remainLen > 0)
                                        {
                                             //把流的位置 设置到接收数据的最后面
                                             m_ReceiveState.Position = fullPacketLen;

                                             //剩余字节缓存器
                                             byte[] remainLenBuffer = new byte[remainLen];

                                             //把剩余的字节数据读取到缓存器
                                             m_ReceiveState.Read(remainLenBuffer, 0, remainLen);
                                             
                                             //重置接收流
                                             m_ReceiveState.SetLength(0);
                                             m_ReceiveState.Position = 0;

                                             //这些数据就相当于到头部了
                                             m_ReceiveState.Write(remainLenBuffer, 0, remainLen);

                                             //清除缓存器
                                             remainLenBuffer = null;
                                             break;
                                        }
                                        else
                                        {
                                             m_ReceiveState.SetLength(0);
                                             m_ReceiveState.Position = 0;
                                             break;
                                        }
                                   }
                                   else
                                   {
                                        //没有收到完整包 继续收
                                        break;
                                   }
                                   
                              }
                         }
                    }

                    //只要数据 > 0 就可以一直收
                    ReceiveMessage();
               }
               else
               {
                    Log.Debug($"接收客户端发送不存在信息{m_ClientSocket.RemoteEndPoint}断开连接");
               }
          }
          catch (Exception e)
          {
               Log.Debug($"捕抓客户端:{m_ClientSocket.RemoteEndPoint}接收异常断开连接 {e}");
          }
     }

     private void OnCheckSendQuene()
     {
          lock (m_SendQuene)
          {
               if (m_SendQuene.Count > 0)
               {
                    SendMessage(m_SendQuene.Dequeue());
               }
          }
     }

     private void Update()
     {
          if (Input.GetKeyDown(KeyCode.C))
          {
               SCLogin scLogin = ReferencePool.Acquire<SCLogin>();
               scLogin.IsCanLogin = false;

               Send(scLogin);
          }
          
          if (Input.GetKeyDown(KeyCode.D))
          {
               Send(ReferencePool.Acquire<SCHeartBeat>());
               //ReferencePool.Release(scLogin);
          }
     }

     public bool Send<T>(T packet) where  T : Packet
     {
          PacketBase packetImpl = packet as PacketBase;
          if (packetImpl == null)
          {
               Log.Warning("Packet is invalid.");
               return false;
          }

          if (packetImpl.PacketType != PacketType.ServerToClient)
          {
               Log.Warning("Send packet invalid.");
               return false;
          }
          
          //TODO这里可以做一系列的处理
                
          //数据包压缩
                
          //数据包crc32验证
                
          //数据包加密验证 
            
          //没看懂为什么要发这么大的包
          //m_SendState.SetLength(m_SendState.Capacity);

          //序列化包体
          m_SendState.Position = PacketHeaderLength;
          Serializer.SerializeWithLengthPrefix(m_SendState, packet, PrefixStyle.Fixed32);

          //包头信息
          SCPacketHeader packetHeader = ReferencePool.Acquire<SCPacketHeader>();
          packetHeader.Id = packet.Id;
          packetHeader.PacketLength = (int)m_SendState.Length - PacketHeaderLength; //消息内容长度需要减去头部消息长度

          Debug.Log($"序列化包头长度{PacketHeaderLength}  序列化包体长度{packetHeader.PacketLength}");
          //序列化
          m_SendState.Position = 0;
          Serializer.SerializeWithLengthPrefix(m_SendState, packetHeader, PrefixStyle.Fixed32);
          
          ReferencePool.Release((IReference)packet);
          ReferencePool.Release(packetHeader);
          //发送消息
          SendMessage(m_SendState.ToArray());

          //归零
          m_SendState.Position = 0;
          m_SendState.SetLength(0);
          return true;
     }

     private void SendMessage(byte[] packet)
     {
          m_ClientSocket.BeginSend(packet, 0, packet.Length, SocketFlags.None, OnSendCallback, m_ClientSocket);
     }

     private void OnSendCallback(IAsyncResult ar)
     {
          try
          {
               m_ClientSocket.EndSend(ar);
               
               //继续检查队列
               m_CheckSendQuene();
          }
          catch (Exception e)
          {
               Debug.Log($"发送消息失败{e}");
          }
     }
     
     //获取客户端-服务器的包类型
     private Type GetClientToServerPacketType(int id)
     {
          Type type = null;
          if (m_ClientToServerPacketTypes.TryGetValue(id, out type))
          {
               return type;
          }
          return null;
     }
}