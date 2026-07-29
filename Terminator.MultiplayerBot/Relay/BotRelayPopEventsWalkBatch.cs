/// <summary>
/// Stack-friendly output for <see cref="BotRelayPopEventsWalkOps"/> multi-frame walks.
/// </summary>
internal struct BotRelayPopEventsWalkBatch
{
    public int appCount;
    public BotRelayPacket app0;
    public BotRelayPacket app1;
    public BotRelayPacket app2;
    public BotRelayPacket app3;
    public BotRelayPacket app4;
    public BotRelayPacket app5;
    public BotRelayPacket app6;
    public BotRelayPacket app7;

    public bool TryAdd(in BotRelayPacket app)
    {
        if (app.IsEmpty || appCount >= BotRelayPopEventsWalkOps.MaxAppsPerWire)
            return false;

        switch (appCount++)
        {
            case 0: app0 = app; return true;
            case 1: app1 = app; return true;
            case 2: app2 = app; return true;
            case 3: app3 = app; return true;
            case 4: app4 = app; return true;
            case 5: app5 = app; return true;
            case 6: app6 = app; return true;
            case 7: app7 = app; return true;
            default: return false;
        }
    }

    public BotRelayPacket GetApp(int index)
    {
        switch (index)
        {
            case 0: return app0;
            case 1: return app1;
            case 2: return app2;
            case 3: return app3;
            case 4: return app4;
            case 5: return app5;
            case 6: return app6;
            case 7: return app7;
            default: return default;
        }
    }

    public void SetApp(int index, in BotRelayPacket app)
    {
        switch (index)
        {
            case 0: app0 = app; break;
            case 1: app1 = app; break;
            case 2: app2 = app; break;
            case 3: app3 = app; break;
            case 4: app4 = app; break;
            case 5: app5 = app; break;
            case 6: app6 = app; break;
            case 7: app7 = app; break;
        }
    }
}
