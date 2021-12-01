﻿
/// <summary>
/// 服务器发送给客户端的 包头
/// </summary>
public sealed class SCPacketHeader : PacketHeaderBase
{
    public override PacketType PacketType
    {
        get
        {
            return PacketType.ServerToClient;
        }
    }

    public override int Id
    {
        get;
        set;
    }

    public override int PacketLength
    {
        get;
        set;
    }
}