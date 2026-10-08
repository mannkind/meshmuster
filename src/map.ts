(function () {
    const element = document.getElementById('map');
    const data = document.getElementById('map-data');
    if (!element || !data || typeof L === 'undefined') return;

    let devices: MapMarker[];
    try {
        devices = JSON.parse(data.textContent ?? '');
    } catch {
        return;
    }
    if (!devices.length) return;

    const colours = {
        update: '#d98324',
        unknown: '#7b5bc4',
        stale: '#c0392b',
        current: '#1a7f45',
    };

    const map = L.map(element, { scrollWheelZoom: false });

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        // Required by the OSM tile policy.
        attribution:
            '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(map);

    function popupFor(d: MapMarker): HTMLElement {
        const el = document.createElement('div');

        const name = document.createElement('strong');
        name.textContent = d.name;
        el.appendChild(name);

        function line(text: string, node?: Node): void {
            el.appendChild(document.createElement('br'));
            if (text) el.appendChild(document.createTextNode(text));
            if (node) el.appendChild(node);
        }

        line(d.role + (d.board ? ' \u00b7 ' + d.board : ''));

        if (d.publicKey) {
            const key = document.createElement('code');
            key.textContent = d.publicKey;
            line('', key);
        }

        line('');
        line('Firmware: ' + d.firmware);
        line('Bootloader: ' + d.bootloader);
        line('');

        const link = document.createElement('a');
        link.href = '/DeviceDetail/' + encodeURIComponent(d.id);
        link.textContent = 'Open device';
        line('', link);

        return el;
    }

    const bounds: L.LatLngTuple[] = [];
    const created: { marker: L.CircleMarker; device: MapMarker }[] = [];

    devices.forEach(function (d) {
        // Status arrives as a plain string, so an unknown value falls back rather than throwing.
        const colour = (colours as Record<string, string | undefined>)[d.status] ?? colours.current;

        const marker = L.circleMarker([d.lat, d.lon], {
            radius: 8,
            color: '#fff',
            weight: 2,
            fillColor: colour,
            fillOpacity: 0.9,
        })
            .addTo(map)
            .bindPopup(popupFor(d));

        created.push({ marker: marker, device: d });
        bounds.push([d.lat, d.lon]);
    });

    // A single node would otherwise fit its bounds at maximum zoom.
    if (bounds.length === 1) {
        map.setView(bounds[0]!, 13);
    } else {
        map.fitBounds(bounds, { padding: [40, 40] });
    }

    created.forEach(function (entry) {
        const el = entry.marker.getElement();
        if (!el) return;
        el.setAttribute('data-device', entry.device.id);
        el.setAttribute('data-status', entry.device.status);
        el.setAttribute('role', 'button');
        el.setAttribute('aria-label', entry.device.name + ' \u2014 ' + entry.device.status);
    });

    // Scroll-zoom only after a click, so the page still scrolls normally.
    map.on('click', function () {
        map.scrollWheelZoom.enable();
    });
    map.on('mouseout', function () {
        map.scrollWheelZoom.disable();
    });
})();
