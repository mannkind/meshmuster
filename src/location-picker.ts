(function () {
    const host = document.getElementById('location-picker');
    const latInput = document.getElementById('Latitude') as HTMLInputElement | null;
    const lonInput = document.getElementById('Longitude') as HTMLInputElement | null;
    if (!host || !latInput || !lonInput || typeof L === 'undefined') return;

    // Six places is a little over 10cm. Anything beyond that is pretending.
    const PLACES = 6;

    const map = L.map(host, { scrollWheelZoom: false });
    map.on('click', function () {
        map.scrollWheelZoom.enable();
    });
    map.on('mouseout', function () {
        map.scrollWheelZoom.disable();
    });
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution:
            '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(map);

    let nearby: NearbyDevice[] = [];
    const nearbyEl = document.getElementById('nearby-devices');
    if (nearbyEl) {
        try {
            nearby = JSON.parse(nearbyEl.textContent ?? '') || [];
        } catch {
            nearby = [];
        }
    }

    // The rest of the mesh, faint and not clickable, purely for bearings.
    nearby.forEach(function (d) {
        L.circleMarker([d.latitude, d.longitude], {
            radius: 5,
            color: '#fff',
            weight: 1,
            fillColor: '#63697a',
            fillOpacity: 0.65,
            interactive: false,
        })
            .addTo(map)
            .bindTooltip(d.name);
    });

    let pin: L.CircleMarker | null = null;

    let writing = false;

    function readInputs(): L.LatLngTuple | null {
        const lat = parseFloat(latInput!.value);
        const lon = parseFloat(lonInput!.value);
        return isFinite(lat) && isFinite(lon) ? [lat, lon] : null;
    }

    function writeInputs(latlng: L.LatLng): void {
        writing = true;
        try {
            latInput!.value = latlng.lat.toFixed(PLACES);
            lonInput!.value = latlng.lng.toFixed(PLACES);
            // Let anything else watching the fields keep up.
            latInput!.dispatchEvent(new Event('change', { bubbles: true }));
            lonInput!.dispatchEvent(new Event('change', { bubbles: true }));
        } finally {
            writing = false;
        }
    }

    function place(latlng: L.LatLngExpression, moveMap: boolean): void {
        if (pin) {
            pin.setLatLng(latlng);
        } else {
            pin = L.circleMarker(latlng, {
                radius: 9,
                color: '#fff',
                weight: 3,
                fillColor: '#2f6feb',
                fillOpacity: 1,
            }).addTo(map);

            pin.on('mousedown', function () {
                map.dragging.disable();
                function follow(e: L.LeafletMouseEvent) {
                    pin!.setLatLng(e.latlng);
                    writeInputs(e.latlng);
                }
                map.on('mousemove', follow);
                map.once('mouseup', function () {
                    map.off('mousemove', follow);
                    map.dragging.enable();
                });
            });
        }

        writeInputs(L.latLng(latlng));
        if (moveMap) map.setView(latlng, Math.max(map.getZoom(), 13));
    }

    map.on('click', function (e) {
        place(e.latlng, false);
    });

    // The original assumed this button existed; a missing one would have thrown here.
    const clear = document.getElementById('location-clear');
    if (clear) {
        clear.addEventListener('click', function () {
            latInput.value = '';
            lonInput.value = '';
            if (pin) {
                map.removeLayer(pin);
                pin = null;
            }
        });
    }

    // Typing a coordinate still wins; the pin follows the fields.
    [latInput, lonInput].forEach(function (input) {
        input.addEventListener('change', function () {
            if (writing) return;
            const coords = readInputs();
            if (coords) place(coords, true);
        });
    });

    const start = readInputs();
    if (start) {
        place(start, false);
        map.setView(start, 13);
    } else if (nearby.length) {
        map.fitBounds(
            nearby.map(function (d): L.LatLngTuple {
                return [d.latitude, d.longitude];
            }),
            { padding: [40, 40], maxZoom: 12 },
        );
    } else {
        map.setView([20, 0], 2);
    }
})();
