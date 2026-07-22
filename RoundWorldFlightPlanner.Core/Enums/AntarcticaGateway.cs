namespace RoundWorldFlightPlanner.Core.Enums;

/// <summary>Which real-world route the itinerary's one mandatory Antarctica landing takes.</summary>
public enum AntarcticaGateway
{
    /// <summary>Via Chile: Santiago -&gt; Puerto Williams -&gt; King George Island (Antarctic Peninsula) -&gt; Buenos Aires. A short Drake Passage hop.</summary>
    ChilePeninsula,

    /// <summary>Via New Zealand: Rotorua -&gt; Christchurch (the real-world McMurdo gateway) -&gt; Williams Field/McMurdo Station -&gt; back through Oceania.</summary>
    NewZealandMcMurdo,
}
