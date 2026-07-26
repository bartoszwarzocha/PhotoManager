namespace PhotoManager.Core.Devices;

/// <summary>Sposób podłączenia urządzenia do komputera.</summary>
public enum DeviceKind
{
    /// <summary>Dysk wymienny / czytnik kart — widoczny jako litera dysku.</summary>
    MassStorage,

    /// <summary>Aparat lub telefon w trybie MTP/PTP — widoczny tylko przez WPD, bez litery dysku.</summary>
    Mtp,
}

/// <summary>
/// Opis wykrytego urządzenia ze zdjęciami. Jest niezmienny i porównywalny po <see cref="Id"/>,
/// dzięki czemu monitor potrafi rozróżnić „to samo urządzenie" od „nowego".
/// </summary>
public sealed record DeviceInfo
{
    /// <summary>Stabilny identyfikator: numer seryjny woluminu (dysk) lub DeviceId z WPD (MTP).</summary>
    public required string Id { get; init; }

    /// <summary>Nazwa czytelna dla użytkownika, np. „Canon EOS R" albo „SD (E:)".</summary>
    public required string Name { get; init; }

    public required DeviceKind Kind { get; init; }

    /// <summary>Dla dysku: ścieżka główna (np. „E:\”). Dla MTP: null.</summary>
    public string? RootPath { get; init; }

    /// <summary>Ścieżka do folderu ze zdjęciami (zwykle DCIM), jeśli udało się ją ustalić.</summary>
    public string? PhotoRoot { get; init; }

    /// <summary>
    /// True, jeśli nośnik zawiera folder DCIM — mocny sygnał, że to karta z aparatu,
    /// a nie zwykły pendrive czy dysk zewnętrzny. Pozwala GUI zaproponować import tylko wtedy,
    /// gdy to ma sens.
    /// </summary>
    public bool HasDcim { get; init; }

    public bool Equals(DeviceInfo? other) => other is not null && Id == other.Id;
    public override int GetHashCode() => Id.GetHashCode();

    public override string ToString()
    {
        var where = Kind == DeviceKind.MassStorage ? RootPath : "MTP";
        return $"[{Kind}] {Name} ({where})";
    }
}
