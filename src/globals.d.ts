// Shapes the server serialises into <script type="application/json"> blocks.
// These are hand-kept mirrors: nothing checks them against the C# at build time.

/** Mirrors MapMarkerView. The short lat/lon names come from its JsonPropertyName attributes. */
interface MapMarker {
    id: string;
    name: string;
    role: string;
    board: string;
    lat: number;
    lon: number;
    firmware: string;
    bootloader: string;
    status: string;
    /** Optional server-side, so guard before use. */
    publicKey?: string | null;
}

/** Mirrors the NearbyDevice record in DeviceEdit.cshtml.cs, which uses default camelCase. */
interface NearbyDevice {
    name: string;
    latitude: number;
    longitude: number;
}
