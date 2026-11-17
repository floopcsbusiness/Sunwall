import AppKit
import CoreImage
import CoreLocation
import ServiceManagement

// MARK: - Math helpers

func rad(_ d: Double) -> Double { d * .pi / 180 }
func deg(_ r: Double) -> Double { r * 180 / .pi }
func clamp(_ x: Double, _ a: Double = 0, _ b: Double = 1) -> Double { min(max(x, a), b) }
func lerp(_ a: Double, _ b: Double, _ t: Double) -> Double { a + (b - a) * t }
func smoothstep(_ e0: Double, _ e1: Double, _ x: Double) -> Double {
    let t = clamp((x - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)
}

struct RGB {
    var r: Double, g: Double, b: Double
    func mixed(_ o: RGB, _ t: Double) -> RGB {
        RGB(r: lerp(r, o.r, t), g: lerp(g, o.g, t), b: lerp(b, o.b, t))
    }
    func scaled(_ k: Double) -> RGB { RGB(r: r * k, g: g * k, b: b * k) }
    func color(_ alpha: Double = 1) -> CGColor {
        CGColor(red: CGFloat(clamp(r)), green: CGFloat(clamp(g)), blue: CGFloat(clamp(b)), alpha: CGFloat(alpha))
    }
}

struct SeededRNG: RandomNumberGenerator {
    var state: UInt64
    mutating func next() -> UInt64 {
        state = state &* 6364136223846793005 &+ 1442695040888963407
        return state
    }
}

// MARK: - Sun position (NOAA-style approximation, good to well under a degree)

struct Sun {
    var azimuth: Double    // degrees clockwise from north
    var elevation: Double  // degrees above horizon
}

func sunPosition(_ date: Date, lat: Double, lon: Double) -> Sun {
    let jd = date.timeIntervalSince1970 / 86400.0 + 2440587.5
    let n = jd - 2451545.0
    let L = (280.460 + 0.9856474 * n).truncatingRemainder(dividingBy: 360)
    let g = rad((357.528 + 0.9856003 * n).truncatingRemainder(dividingBy: 360))
    let lambda = rad(L + 1.915 * sin(g) + 0.020 * sin(2 * g))
    let eps = rad(23.439 - 0.0000004 * n)
    let ra = atan2(cos(eps) * sin(lambda), cos(lambda))
    let dec = asin(sin(eps) * sin(lambda))
    let gmst = (280.46061837 + 360.98564736629 * n).truncatingRemainder(dividingBy: 360)
    let ha = rad(gmst + lon) - ra
    let phi = rad(lat)
    let sinEl = sin(phi) * sin(dec) + cos(phi) * cos(dec) * cos(ha)
    let el = asin(max(-1, min(1, sinEl)))
    let az = atan2(sin(ha), cos(ha) * sin(phi) - tan(dec) * cos(phi)) + Double.pi
    var azDeg = deg(az).truncatingRemainder(dividingBy: 360)
    if azDeg < 0 { azDeg += 360 }
    return Sun(azimuth: azDeg, elevation: deg(el))
}

func sunTimes(for day: Date, lat: Double, lon: Double) -> (rise: Date?, set: Date?) {
    let start = Calendar.current.startOfDay(for: day)
    var rise: Date? = nil
    var set: Date? = nil
    var prev = sunPosition(start, lat: lat, lon: lon).elevation
    for m in 1...1440 {
        let t = start.addingTimeInterval(Double(m) * 60)
        let e = sunPosition(t, lat: lat, lon: lon).elevation
        if rise == nil && prev < -0.833 && e >= -0.833 { rise = t }
        if set == nil && prev >= -0.833 && e < -0.833 { set = t }
        prev = e
    }
    return (rise, set)
}

// MARK: - Model

enum Preset: String, CaseIterable {
    case olive = "Olive"
    case blinds = "Blinds"
    case prism = "Prism"
    case suncatcher = "Suncatcher"
    case arch = "Arch"
    case komorebi = "Komorebi"
    case tidal = "Tidal"
    case rainlight = "Rainlight"
    case copper = "Copper"
    case opaline = "Opaline"
    case jali = "Jali"
    case shoji = "Shoji"
}

final class Model {
    var preset: Preset
    var lat: Double
    var lon: Double
    var autoLocation: Bool
    var scrub: Double? = nil      // minutes since local midnight; nil = live
    var cloudMode: Int            // 0 live, 1 clear, 2 partly, 3 overcast
    var shadowMode: Int           // 0 off, 1 soft, 2 deep
    var brightness: Double        // 0.3...1.8
    var opacity: Double           // 0.1...1
    var replaceMode: Bool         // false = overlay on wallpaper, true = solid wall
    var liveCloud: Double = 0     // 0...1 from weather API

    init() {
        let d = UserDefaults.standard
        preset = Preset(rawValue: d.string(forKey: "preset") ?? "") ?? .blinds
        // Fallback until the Mac reports a real location (Richmond, VA area). Change via "Set location…".
        lat = d.object(forKey: "lat") as? Double ?? 37.54
        lon = d.object(forKey: "lon") as? Double ?? -77.43
        autoLocation = d.object(forKey: "autoLocation") as? Bool ?? true
        cloudMode = d.object(forKey: "cloudMode") as? Int ?? 0
        shadowMode = d.object(forKey: "shadowMode") as? Int ?? 1
        brightness = d.object(forKey: "brightness") as? Double ?? 1.0
        opacity = d.object(forKey: "opacity") as? Double ?? 1.0
        replaceMode = d.object(forKey: "replaceMode") as? Bool ?? false
    }

    func save() {
        let d = UserDefaults.standard
        d.set(preset.rawValue, forKey: "preset")
        d.set(lat, forKey: "lat")
        d.set(lon, forKey: "lon")
        d.set(autoLocation, forKey: "autoLocation")
        d.set(cloudMode, forKey: "cloudMode")
        d.set(shadowMode, forKey: "shadowMode")
        d.set(brightness, forKey: "brightness")
        d.set(opacity, forKey: "opacity")
        d.set(replaceMode, forKey: "replaceMode")
    }

    var date: Date {
        if let m = scrub {
            return Calendar.current.startOfDay(for: Date()).addingTimeInterval(m * 60)
        }
        return Date()
    }

    var shadowDepth: Double {
        switch shadowMode {
        case 1: return 0.30
        case 2: return 0.50
        default: return 0
        }
    }

    var cloud: Double {
        switch cloudMode {
        case 1: return 0
        case 2: return 0.5
        case 3: return 0.95
        default: return liveCloud
        }
    }
}

// MARK: - Rendering

func hueColor(_ h: Double, _ s: Double = 0.55, _ b: Double = 1.0, _ a: Double = 1.0) -> CGColor {
    NSColor(calibratedHue: CGFloat(h), saturation: CGFloat(s), brightness: CGFloat(b), alpha: CGFloat(a)).cgColor
}

func fillGrid(_ c: CGContext, bw: Double, bh: Double, cols: Int, rows: Int, gap: Double) {
    let pw = (bw - gap * Double(cols - 1)) / Double(cols)
    let ph = (bh - gap * Double(rows - 1)) / Double(rows)
    for r in 0..<rows {
        for q in 0..<cols {
            c.fill(CGRect(x: -bw / 2 + Double(q) * (pw + gap),
                          y: -bh / 2 + Double(r) * (ph + gap),
                          width: pw, height: ph))
        }
    }
}

func clearLeaf(_ c: CGContext, x: Double, y: Double, length: Double, width: Double, angle: Double) {
    c.saveGState()
    c.translateBy(x: CGFloat(x), y: CGFloat(y))
    c.rotate(by: CGFloat(angle))
    c.setBlendMode(.clear)
    c.fillEllipse(in: CGRect(x: -length / 2, y: -width / 2, width: length, height: width))
    c.restoreGState()
}

func clearBranch(_ c: CGContext, from p0: CGPoint, via p1: CGPoint, to p2: CGPoint, leafLen: Double, count: Int) {
    c.saveGState()
    c.setBlendMode(.clear)
    c.setLineWidth(CGFloat(leafLen * 0.07))
    c.setLineCap(.round)
    c.move(to: p0)
    c.addQuadCurve(to: p2, control: p1)
    c.strokePath()
    c.restoreGState()
    for i in 1...count {
        let t = Double(i) / Double(count + 1)
        let mt = 1 - t
        let x = mt * mt * Double(p0.x) + 2 * mt * t * Double(p1.x) + t * t * Double(p2.x)
        let y = mt * mt * Double(p0.y) + 2 * mt * t * Double(p1.y) + t * t * Double(p2.y)
        let dx = 2 * mt * Double(p1.x - p0.x) + 2 * t * Double(p2.x - p1.x)
        let dy = 2 * mt * Double(p1.y - p0.y) + 2 * t * Double(p2.y - p1.y)
        let ang = atan2(dy, dx)
        let side: Double = (i % 2 == 0) ? 0.8 : -0.8
        clearLeaf(c,
                  x: x + cos(ang + side) * leafLen * 0.5,
                  y: y + sin(ang + side) * leafLen * 0.5,
                  length: leafLen, width: leafLen * 0.38, angle: ang + side)
    }
}

func drawPreset(_ c: CGContext, preset: Preset, bw: Double, bh: Double, elevN: Double, light: RGB) {
    var rng = SeededRNG(state: 42)
    switch preset {

    case .olive:
        fillGrid(c, bw: bw, bh: bh, cols: 2, rows: 2, gap: bw * 0.045)
        clearBranch(c,
                    from: CGPoint(x: bw * 0.5, y: -bh * 0.5),
                    via: CGPoint(x: bw * 0.05, y: -bh * 0.05),
                    to: CGPoint(x: -bw * 0.15, y: bh * 0.4),
                    leafLen: bw * 0.16, count: 11)

    case .blinds:
        let n = 15
        let slat = bh / Double(n)
        let open = 0.30 + 0.40 * elevN
        for i in 0..<n {
            c.fill(CGRect(x: -bw / 2, y: -bh / 2 + Double(i) * slat, width: bw, height: slat * open))
        }

    case .prism:
        let path = CGMutablePath()
        path.move(to: CGPoint(x: -bw * 0.30, y: -bh * 0.50))
        path.addLine(to: CGPoint(x: bw * 0.42, y: -bh * 0.30))
        path.addLine(to: CGPoint(x: bw * 0.28, y: bh * 0.50))
        path.addLine(to: CGPoint(x: -bw * 0.44, y: bh * 0.30))
        path.closeSubpath()
        c.addPath(path)
        c.fillPath()
        c.saveGState()
        c.translateBy(x: CGFloat(-bw * 0.52), y: CGFloat(-bh * 0.18))
        c.rotate(by: 0.5)
        for i in 0..<7 {
            c.setFillColor(hueColor(Double(i) / 7.0 * 0.8, 0.85, 1.0, 0.9))
            c.fill(CGRect(x: Double(i) * bw * 0.018, y: 0, width: bw * 0.018, height: bh * 0.20))
        }
        c.restoreGState()
        c.saveGState()
        c.setBlendMode(.clear)
        c.setLineWidth(CGFloat(bw * 0.012))
        c.move(to: CGPoint(x: 0, y: bh * 0.5))
        c.addLine(to: CGPoint(x: bw * 0.2, y: -bh * 0.1))
        c.strokePath()
        c.restoreGState()

    case .suncatcher:
        fillGrid(c, bw: bw, bh: bh, cols: 2, rows: 2, gap: bw * 0.05)
        for _ in 0..<16 {
            let x = Double.random(in: bw * 0.45 ... bw * 0.85, using: &rng)
            let y = Double.random(in: -bh * 0.5 ... bh * 0.5, using: &rng)
            let r = Double.random(in: 0.01 ... 0.03, using: &rng) * bw
            c.setFillColor(hueColor(Double.random(in: 0 ... 1, using: &rng), 0.45, 1.0, 0.8))
            c.fillEllipse(in: CGRect(x: x - r, y: y - r, width: r * 2, height: r * 2))
        }

    case .arch:
        let hw = bw * 0.34
        let archY = bh * 0.15
        let path = CGMutablePath()
        path.move(to: CGPoint(x: -hw, y: -bh * 0.5))
        path.addLine(to: CGPoint(x: hw, y: -bh * 0.5))
        path.addLine(to: CGPoint(x: hw, y: archY))
        path.addArc(center: CGPoint(x: 0, y: archY), radius: CGFloat(hw),
                    startAngle: 0, endAngle: CGFloat.pi, clockwise: false)
        path.closeSubpath()
        c.addPath(path)
        c.fillPath()
        c.saveGState()
        c.setBlendMode(.clear)
        c.fill(CGRect(x: -bw * 0.012, y: -bh * 0.5, width: bw * 0.024, height: bh * 0.5 + archY + hw))
        c.fill(CGRect(x: -hw, y: -bh * 0.02, width: hw * 2, height: bw * 0.024))
        c.restoreGState()

    case .komorebi:
        for _ in 0..<170 {
            let x = Double.random(in: -bw * 0.65 ... bw * 0.65, using: &rng)
            let y = Double.random(in: -bh * 0.65 ... bh * 0.65, using: &rng)
            let a = Double.random(in: 0.04 ... 0.13, using: &rng) * bw
            let b = a * Double.random(in: 0.55 ... 1.0, using: &rng)
            let rot = Double.random(in: 0 ... Double.pi, using: &rng)
            let alpha = Double.random(in: 0.55 ... 1.0, using: &rng)
            c.saveGState()
            c.setAlpha(CGFloat(alpha))
            c.translateBy(x: CGFloat(x), y: CGFloat(y))
            c.rotate(by: CGFloat(rot))
            c.fillEllipse(in: CGRect(x: -a / 2, y: -b / 2, width: a, height: b))
            c.restoreGState()
        }

    case .tidal:
        c.setStrokeColor(light.color(0.75))
        c.setLineWidth(CGFloat(bh * 0.010))
        c.setLineCap(.round)
        let rows = 34
        for i in 0..<rows {
            let y0 = -bh * 0.7 + Double(i) * (bh * 1.4) / Double(rows)
            let ph = Double.random(in: 0 ... 6.28, using: &rng)
            let amp = bh * Double.random(in: 0.012 ... 0.035, using: &rng)
            let fq = Double.random(in: 7 ... 14, using: &rng) / bw
            var x = -bw * 0.7
            c.beginPath()
            c.move(to: CGPoint(x: x, y: y0 + sin(x * fq + ph) * amp))
            while x < bw * 0.7 {
                x += bw * 0.03
                let y = y0 + sin(x * fq + ph) * amp + sin(x * fq * 2.3 + ph * 1.7) * amp * 0.5
                c.addLine(to: CGPoint(x: x, y: y))
            }
            c.strokePath()
        }

    case .rainlight:
        fillGrid(c, bw: bw, bh: bh, cols: 2, rows: 3, gap: bw * 0.04)
        c.saveGState()
        c.setBlendMode(.clear)
        for _ in 0..<80 {
            let x = Double.random(in: -bw / 2 ... bw / 2, using: &rng)
            let y = Double.random(in: -bh / 2 ... bh / 2, using: &rng)
            let r = Double.random(in: 0.006 ... 0.016, using: &rng) * bw
            c.fillEllipse(in: CGRect(x: x - r, y: y - r * 1.8, width: r * 2, height: r * 3.6))
        }
        for _ in 0..<10 {
            let x = Double.random(in: -bw / 2 ... bw / 2, using: &rng)
            let y = Double.random(in: -bh / 2 ... bh / 2, using: &rng)
            let len = Double.random(in: 0.1 ... 0.3, using: &rng) * bh
            c.fill(CGRect(x: x, y: y, width: bw * 0.006, height: len))
        }
        c.restoreGState()

    case .copper:
        let copper = light.mixed(RGB(r: 1.0, g: 0.70, b: 0.42), 0.6)
        for _ in 0..<10 {
            let x = Double.random(in: -bw * 0.3 ... bw * 0.3, using: &rng)
            let y = Double.random(in: -bh * 0.5 ... bh * 0.5, using: &rng)
            let l = Double.random(in: 0.5 ... 1.0, using: &rng) * bw
            let t = Double.random(in: 0.025 ... 0.06, using: &rng) * bh
            c.setFillColor(copper.color(Double.random(in: 0.5 ... 0.9, using: &rng)))
            c.fillEllipse(in: CGRect(x: x - l / 2, y: y - t / 2, width: l, height: t))
        }

    case .opaline:
        let palette: [RGB] = [
            RGB(r: 0.55, g: 0.75, b: 1.0),
            RGB(r: 1.0, g: 0.92, b: 0.55),
            RGB(r: 1.0, g: 1.0, b: 1.0),
            RGB(r: 0.95, g: 0.65, b: 0.80),
            RGB(r: 0.70, g: 0.95, b: 0.85)
        ]
        let cols = 3
        let rows = 3
        let gap = bw * 0.03
        let pw = (bw - gap * Double(cols - 1)) / Double(cols)
        let ph = (bh - gap * Double(rows - 1)) / Double(rows)
        var idx = 0
        for r in 0..<rows {
            for q in 0..<cols {
                c.setFillColor(palette[(idx * 2 + r) % palette.count].color(0.9))
                idx += 1
                c.fill(CGRect(x: -bw / 2 + Double(q) * (pw + gap),
                              y: -bh / 2 + Double(r) * (ph + gap),
                              width: pw, height: ph))
            }
        }

    case .jali:
        let cols = 11
        let rowsN = 16
        let pitchX = bw * 1.3 / Double(cols)
        let pitchY = bh * 1.3 / Double(rowsN)
        for r in 0..<rowsN {
            for q in 0..<cols {
                let off = (r % 2 == 0) ? 0.0 : 0.5
                let cx = -bw * 0.65 + (Double(q) + off + 0.5) * pitchX
                let cy = -bh * 0.65 + (Double(r) + 0.5) * pitchY
                let path = CGMutablePath()
                path.move(to: CGPoint(x: cx, y: cy + pitchY * 0.42))
                path.addLine(to: CGPoint(x: cx + pitchX * 0.30, y: cy))
                path.addLine(to: CGPoint(x: cx, y: cy - pitchY * 0.42))
                path.addLine(to: CGPoint(x: cx - pitchX * 0.30, y: cy))
                path.closeSubpath()
                c.addPath(path)
                c.fillPath()
            }
        }

    case .shoji:
        c.setAlpha(0.88)
        fillGrid(c, bw: bw * 0.8, bh: bh, cols: 3, rows: 5, gap: bw * 0.022)
    }
}

let ciContext = CIContext()

func renderWall(_ ctx: CGContext, size: CGSize, model: Model, scale: Double) {
    let w = Double(size.width)
    let h = Double(size.height)
    let full = CGRect(x: 0, y: 0, width: w, height: h)
    let cs = CGColorSpaceCreateDeviceRGB()
    let date = model.date
    let sun = sunPosition(date, lat: model.lat, lon: model.lon)
    let el = sun.elevation
    let cloud = model.cloud
    let times = sunTimes(for: date, lat: model.lat, lon: model.lon)

    // Progress through the day, 0 at sunrise, 1 at sunset. Drives the patch left to right.
    let p: Double
    if let r = times.rise, let s = times.set, s > r {
        p = clamp(date.timeIntervalSince(r) / s.timeIntervalSince(r))
    } else {
        p = clamp((sun.azimuth - 90) / 180)
    }

    let day = smoothstep(-8, 6, el)
    let elevN = clamp(el / 60)
    let warm = 1 - smoothstep(0, 25, el)

    // Transparent canvas: the user's own wallpaper shows through everything we don't paint.
    ctx.clear(full)
    let replace = model.replaceMode

    // Full wallpaper mode: paint our own wall over the whole screen.
    if replace {
        let nightWall = RGB(r: 0.035, g: 0.045, b: 0.085)
        let dayWall = RGB(r: 0.58, g: 0.54, b: 0.49)
        let ambientDay = dayWall.mixed(RGB(r: 0.60, g: 0.42, b: 0.36), warm * 0.55)
        var wall = nightWall.mixed(ambientDay, day)
        wall = wall.mixed(RGB(r: 0.45, g: 0.45, b: 0.47), cloud * 0.35 * day)
        wall = wall.scaled(1 - 0.10 * cloud * day)
        let top = wall.scaled(0.86).color()
        let bottom = wall.scaled(1.08).color()
        if let grad = CGGradient(colorsSpace: cs, colors: [top, bottom] as CFArray, locations: [0, 1]) {
            ctx.drawLinearGradient(grad, start: CGPoint(x: 0, y: h), end: CGPoint(x: 0, y: 0), options: [])
        } else {
            ctx.setFillColor(wall.color())
            ctx.fill(full)
        }
    }

    // Night and dusk tint
    let night = 1 - day
    if !replace && night > 0.01 {
        ctx.setFillColor(RGB(r: 0.03, g: 0.05, b: 0.13).color(0.62 * night))
        ctx.fill(full)
    }

    // Golden hour tint
    let golden = warm * day * (1 - cloud * 0.7)
    if !replace && golden > 0.01 {
        ctx.setFillColor(RGB(r: 1.0, g: 0.55, b: 0.25).color(0.10 * golden))
        ctx.fill(full)
    }

    // Room shadow: dims the wallpaper everywhere, then the sunlight patch is cut out of it.
    let direct = smoothstep(-0.5, 8, el) * (1 - 0.88 * cloud)
    let shadowA = replace ? 0 : model.shadowDepth * day * (1 - 0.8 * cloud)
    let shaded = shadowA > 0.01
    if shaded {
        ctx.setFillColor(RGB(r: 0.05, g: 0.04, b: 0.08).color(shadowA))
        ctx.fill(full)
    }

    if direct > 0.01 {
        let lowC = RGB(r: 1.0, g: 0.55, b: 0.28)
        let midC = RGB(r: 1.0, g: 0.82, b: 0.60)
        let highC = RGB(r: 1.0, g: 0.95, b: 0.86)
        let light = lowC.mixed(midC, smoothstep(0, 12, el)).mixed(highC, smoothstep(12, 45, el))
            .mixed(RGB(r: 1, g: 1, b: 1), clamp((model.brightness - 1) * 0.5))

        let relAz = rad((p - 0.5) * 2 * 68)
        let xc = w * (0.5 + sin(relAz) * 0.38)
        let yc = h * (1 - (0.30 + 0.32 * elevN))
        let bw = w * 0.46
        let bh = h * 0.60
        let stretch = 1 + 0.9 * (1 - elevN)
        let shear = tan(relAz) * 0.45
        let tilt = rad(-5)

        // Warm glow around the patch
        if let glow = CGGradient(colorsSpace: cs,
                                 colors: [light.color(clamp((replace ? 0.22 : 0.14) * direct * model.brightness)), light.color(0)] as CFArray,
                                 locations: [0, 1]) {
            ctx.drawRadialGradient(glow,
                                   startCenter: CGPoint(x: xc, y: yc), startRadius: 0,
                                   endCenter: CGPoint(x: xc, y: yc), endRadius: CGFloat(w * 0.55),
                                   options: [])
        }

        // Render the light shape at near-native resolution, then blur it properly.
        let ps = max(1.0, scale) * 0.5
        let sw = max(16, Int(w * ps))
        let sh = max(16, Int(h * ps))
        let sigma = (3 + 14 * cloud + 5 * (1 - elevN)) * ps
        if let small = CGContext(data: nil, width: sw, height: sh, bitsPerComponent: 8, bytesPerRow: 0,
                                 space: cs, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) {
            small.scaleBy(x: CGFloat(ps), y: CGFloat(ps))
            small.translateBy(x: CGFloat(xc), y: CGFloat(yc))
            small.concatenate(CGAffineTransform(rotationAngle: CGFloat(tilt)))
            small.concatenate(CGAffineTransform(a: 1, b: 0, c: CGFloat(shear), d: CGFloat(stretch), tx: 0, ty: 0))
            small.setFillColor(light.color())
            drawPreset(small, preset: model.preset, bw: bw, bh: bh, elevN: elevN, light: light)
            if let img = small.makeImage() {
                let ci = CIImage(cgImage: img)
                let blurred = ci.clampedToExtent()
                    .applyingGaussianBlur(sigma: sigma)
                    .cropped(to: ci.extent)
                let finalImg = ciContext.createCGImage(blurred, from: ci.extent) ?? img

                // Cut the lit area out of the shadow layer
                if shaded {
                    ctx.saveGState()
                    ctx.setBlendMode(.destinationOut)
                    ctx.setAlpha(CGFloat(clamp(direct * model.brightness)))
                    ctx.draw(finalImg, in: full)
                    ctx.restoreGState()
                }

                // Warm light on top
                ctx.saveGState()
                ctx.interpolationQuality = .high
                ctx.setAlpha(CGFloat(clamp(direct * (replace ? 0.62 : (shaded ? 0.22 : 0.34)) * model.brightness, 0, 0.95)))
                ctx.draw(finalImg, in: full)
                ctx.restoreGState()
            }
        }
    }

    // Vignette (full wallpaper mode only)
    if replace {
        let vClear = CGColor(gray: 0, alpha: 0)
        let vDark = CGColor(gray: 0, alpha: 0.30)
        if let vg = CGGradient(colorsSpace: cs, colors: [vClear, vClear, vDark] as CFArray, locations: [0, 0.55, 1]) {
            let center = CGPoint(x: w / 2, y: h / 2)
            ctx.drawRadialGradient(vg, startCenter: center, startRadius: 0, endCenter: center,
                                   endRadius: CGFloat(hypot(w, h) * 0.6), options: [])
        }
    }
}

final class WallView: NSView {
    let model: Model

    init(frame: NSRect, model: Model) {
        self.model = model
        super.init(frame: frame)
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    override var isOpaque: Bool { false }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let s = Double(window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2)
        renderWall(ctx, size: bounds.size, model: model, scale: s)
    }
}

// MARK: - App

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate, CLLocationManagerDelegate {
    let model = Model()
    var windows: [NSWindow] = []
    var statusItem: NSStatusItem!
    let locationManager = CLLocationManager()

    var infoItem = NSMenuItem()
    var sunItem = NSMenuItem()
    var liveItem = NSMenuItem()
    var loginItem = NSMenuItem()
    var presetItems: [NSMenuItem] = []
    var cloudItems: [NSMenuItem] = []
    var shadowItems: [NSMenuItem] = []
    var replaceItem = NSMenuItem()
    var slider = NSSlider()
    var brightnessSlider = NSSlider()
    var opacitySlider = NSSlider()
    var timeLabel = NSTextField()
    let popover = NSPopover()

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        buildWindows()
        buildMenu()
        buildPopover()

        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                               object: nil, queue: .main) { [weak self] _ in
            self?.buildWindows()
        }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification,
                                                          object: nil, queue: .main) { [weak self] _ in
            self?.fetchWeather()
            self?.refresh()
        }

        Timer.scheduledTimer(withTimeInterval: 20, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            if self.model.scrub == nil { self.refresh() }
        }
        Timer.scheduledTimer(withTimeInterval: 15 * 60, repeats: true) { [weak self] _ in
            self?.fetchWeather()
        }

        locationManager.delegate = self
        locationManager.desiredAccuracy = kCLLocationAccuracyThreeKilometers
        if model.autoLocation {
            locationManager.requestWhenInUseAuthorization()
            locationManager.startUpdatingLocation()
        }
        fetchWeather()
        refresh()
    }

    // MARK: Windows

    func buildWindows() {
        windows.forEach { $0.orderOut(nil) }
        windows.removeAll()
        for screen in NSScreen.screens {
            let win = NSWindow(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
            win.level = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopWindow)) + 1)
            win.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
            win.isOpaque = false
            win.hasShadow = false
            win.ignoresMouseEvents = true
            win.isReleasedWhenClosed = false
            win.backgroundColor = .clear
            win.alphaValue = CGFloat(model.opacity)
            win.contentView = WallView(frame: NSRect(origin: .zero, size: screen.frame.size), model: model)
            win.setFrame(screen.frame, display: true)
            win.orderBack(nil)
            windows.append(win)
        }
    }

    func refresh() {
        for w in windows {
            w.alphaValue = CGFloat(model.opacity)
            w.contentView?.needsDisplay = true
        }
        updateLabels()
    }

    // MARK: Menu

    func buildMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "sun.max.fill", accessibilityDescription: "Sunwall")

        let menu = NSMenu()
        menu.delegate = self

        infoItem.isEnabled = false
        sunItem.isEnabled = false
        menu.addItem(infoItem)
        menu.addItem(sunItem)
        menu.addItem(.separator())

        let lookRoot = NSMenuItem(title: "Look", action: nil, keyEquivalent: "")
        let lookMenu = NSMenu()
        for p in Preset.allCases {
            let it = NSMenuItem(title: p.rawValue, action: #selector(pickPreset(_:)), keyEquivalent: "")
            it.target = self
            it.representedObject = p.rawValue
            presetItems.append(it)
            lookMenu.addItem(it)
        }
        lookRoot.submenu = lookMenu
        menu.addItem(lookRoot)
        menu.addItem(.separator())

        let slidersItem = NSMenuItem(title: "Sliders…", action: #selector(showSliders), keyEquivalent: "")
        slidersItem.target = self
        menu.addItem(slidersItem)

        liveItem = NSMenuItem(title: "Follow real time", action: #selector(followLive), keyEquivalent: "")
        liveItem.target = self
        menu.addItem(liveItem)
        menu.addItem(.separator())

        let cloudRoot = NSMenuItem(title: "Clouds", action: nil, keyEquivalent: "")
        let cloudMenu = NSMenu()
        let names = ["Live weather", "Clear", "Partly cloudy", "Overcast"]
        for (i, n) in names.enumerated() {
            let it = NSMenuItem(title: n, action: #selector(pickCloud(_:)), keyEquivalent: "")
            it.target = self
            it.tag = i
            cloudItems.append(it)
            cloudMenu.addItem(it)
        }
        cloudRoot.submenu = cloudMenu
        menu.addItem(cloudRoot)

        let shadowRoot = NSMenuItem(title: "Shadows", action: nil, keyEquivalent: "")
        let shadowMenu = NSMenu()
        for (i, n) in ["Off (light only)", "Soft", "Deep"].enumerated() {
            let it = NSMenuItem(title: n, action: #selector(pickShadow(_:)), keyEquivalent: "")
            it.target = self
            it.tag = i
            shadowItems.append(it)
            shadowMenu.addItem(it)
        }
        shadowRoot.submenu = shadowMenu
        menu.addItem(shadowRoot)

        replaceItem = NSMenuItem(title: "Replace wallpaper (full mode)", action: #selector(toggleReplace), keyEquivalent: "")
        replaceItem.target = self
        menu.addItem(replaceItem)

        let locItem = NSMenuItem(title: "Set location…", action: #selector(setLocation), keyEquivalent: "")
        locItem.target = self
        menu.addItem(locItem)

        loginItem = NSMenuItem(title: "Launch at login", action: #selector(toggleLogin), keyEquivalent: "")
        loginItem.target = self
        menu.addItem(loginItem)

        menu.addItem(.separator())
        let quit = NSMenuItem(title: "Quit Sunwall", action: #selector(quitApp), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        statusItem.menu = menu
    }

    func addSliderRow(_ v: NSView, label: String, y: CGFloat, lo: Double, hi: Double,
                      value: Double, action: Selector) -> (NSSlider, NSTextField) {
        let l = NSTextField(labelWithString: label)
        l.font = NSFont.systemFont(ofSize: 11)
        l.textColor = NSColor.secondaryLabelColor
        l.frame = NSRect(x: 16, y: y + 26, width: 228, height: 14)
        let sl = NSSlider(value: value, minValue: lo, maxValue: hi, target: self, action: action)
        sl.frame = NSRect(x: 16, y: y + 4, width: 228, height: 20)
        sl.isContinuous = true
        v.addSubview(l)
        v.addSubview(sl)
        return (sl, l)
    }

    func buildPopover() {
        let vc = NSViewController()
        let v = NSView(frame: NSRect(x: 0, y: 0, width: 260, height: 210))

        let (op, _) = addSliderRow(v, label: "Opacity", y: 12, lo: 0.1, hi: 1.0,
                                   value: model.opacity, action: #selector(opacityMoved(_:)))
        opacitySlider = op
        let (br, _) = addSliderRow(v, label: "Brightness", y: 64, lo: 0.3, hi: 1.8,
                                   value: model.brightness, action: #selector(brightnessMoved(_:)))
        brightnessSlider = br
        let (tm, tl) = addSliderRow(v, label: "Time of day", y: 116, lo: 0, hi: 1439,
                                    value: 0, action: #selector(sliderMoved(_:)))
        slider = tm
        timeLabel = tl

        let live = NSButton(title: "Follow real time", target: self, action: #selector(followLive))
        live.bezelStyle = .rounded
        live.frame = NSRect(x: 16, y: 172, width: 228, height: 28)
        v.addSubview(live)

        vc.view = v
        popover.contentViewController = vc
        popover.behavior = .transient
    }

    func updateTimeLabel(_ mins: Double) {
        let m = Int(mins)
        timeLabel.stringValue = String(format: "Time of day  %02ld:%02ld", m / 60, m % 60)
    }

    func syncSliders() {
        let c = Calendar.current.dateComponents([.hour, .minute], from: model.date)
        let mins = Double((c.hour ?? 0) * 60 + (c.minute ?? 0))
        slider.doubleValue = mins
        brightnessSlider.doubleValue = model.brightness
        opacitySlider.doubleValue = model.opacity
        updateTimeLabel(mins)
    }

    @objc func showSliders() {
        guard let button = statusItem.button else { return }
        syncSliders()
        NSApp.activate(ignoringOtherApps: true)
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
    }

    func menuWillOpen(_ menu: NSMenu) {
        replaceItem.state = model.replaceMode ? .on : .off
        brightnessSlider.doubleValue = model.brightness
        opacitySlider.doubleValue = model.opacity
        for it in presetItems {
            it.state = (it.representedObject as? String) == model.preset.rawValue ? .on : .off
        }
        for it in cloudItems {
            it.state = it.tag == model.cloudMode ? .on : .off
        }
        for it in shadowItems {
            it.state = it.tag == model.shadowMode ? .on : .off
        }
        liveItem.state = model.scrub == nil ? .on : .off
        loginItem.state = SMAppService.mainApp.status == .enabled ? .on : .off
        let c = Calendar.current.dateComponents([.hour, .minute], from: model.date)
        slider.doubleValue = Double((c.hour ?? 0) * 60 + (c.minute ?? 0))
        updateLabels()
    }

    func updateLabels() {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "HH:mm"
        let date = model.date
        let sun = sunPosition(date, lat: model.lat, lon: model.lon)
        let times = sunTimes(for: date, lat: model.lat, lon: model.lon)
        let sky = Int((model.cloud * 100).rounded())
        infoItem.title = "\(f.string(from: date))  ·  sun \(Int(sun.elevation.rounded()))° up  ·  clouds \(sky)%"
        if let r = times.rise, let s = times.set {
            sunItem.title = "Sunrise \(f.string(from: r))  ·  Sunset \(f.string(from: s))"
        } else {
            sunItem.title = "No sunrise or sunset today"
        }
    }

    // MARK: Actions

    @objc func pickPreset(_ sender: NSMenuItem) {
        if let raw = sender.representedObject as? String, let p = Preset(rawValue: raw) {
            model.preset = p
            model.save()
            refresh()
        }
    }

    @objc func pickCloud(_ sender: NSMenuItem) {
        model.cloudMode = sender.tag
        model.save()
        refresh()
    }

    @objc func toggleReplace() {
        model.replaceMode.toggle()
        model.save()
        refresh()
    }

    @objc func pickShadow(_ sender: NSMenuItem) {
        model.shadowMode = sender.tag
        model.save()
        refresh()
    }

    @objc func brightnessMoved(_ sender: NSSlider) {
        model.brightness = sender.doubleValue
        model.save()
        refresh()
    }

    @objc func opacityMoved(_ sender: NSSlider) {
        model.opacity = sender.doubleValue
        model.save()
        refresh()
    }

    @objc func sliderMoved(_ sender: NSSlider) {
        model.scrub = sender.doubleValue
        updateTimeLabel(sender.doubleValue)
        refresh()
    }

    @objc func followLive() {
        model.scrub = nil
        syncSliders()
        refresh()
    }

    @objc func toggleLogin() {
        let s = SMAppService.mainApp
        do {
            if s.status == .enabled { try s.unregister() } else { try s.register() }
        } catch {
            NSSound.beep()
        }
    }

    @objc func quitApp() {
        NSApp.terminate(nil)
    }

    @objc func setLocation() {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Set location"
        alert.informativeText = "Latitude and longitude in decimal degrees (west and south are negative). Leave both empty to use your Mac's location."
        let latF = NSTextField(frame: NSRect(x: 0, y: 30, width: 260, height: 24))
        let lonF = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
        latF.placeholderString = "Latitude, e.g. 48.137"
        lonF.placeholderString = "Longitude, e.g. 11.575"
        if !model.autoLocation {
            latF.stringValue = String(model.lat)
            lonF.stringValue = String(model.lon)
        }
        let box = NSView(frame: NSRect(x: 0, y: 0, width: 260, height: 58))
        box.addSubview(latF)
        box.addSubview(lonF)
        alert.accessoryView = box
        alert.addButton(withTitle: "Save")
        alert.addButton(withTitle: "Cancel")
        guard alert.runModal() == .alertFirstButtonReturn else { return }

        let la = latF.stringValue.trimmingCharacters(in: .whitespaces)
        let lo = lonF.stringValue.trimmingCharacters(in: .whitespaces)
        if la.isEmpty && lo.isEmpty {
            model.autoLocation = true
            model.save()
            locationManager.requestWhenInUseAuthorization()
            locationManager.startUpdatingLocation()
        } else if let a = Double(la), let b = Double(lo), abs(a) <= 90, abs(b) <= 180 {
            model.autoLocation = false
            model.lat = a
            model.lon = b
            model.save()
            fetchWeather()
            refresh()
        } else {
            NSSound.beep()
        }
    }

    // MARK: Location

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard model.autoLocation, let l = locations.last else { return }
        model.lat = l.coordinate.latitude
        model.lon = l.coordinate.longitude
        model.save()
        manager.stopUpdatingLocation()
        fetchWeather()
        refresh()
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        // Stay on the last known or fallback coordinates.
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let s = manager.authorizationStatus
        if model.autoLocation && s == .authorizedAlways {
            manager.startUpdatingLocation()
        }
    }

    // MARK: Weather (Open-Meteo, no API key)

    func fetchWeather() {
        guard model.cloudMode == 0 else { return }
        let urlString = "https://api.open-meteo.com/v1/forecast?latitude=\(model.lat)&longitude=\(model.lon)&current=cloud_cover"
        guard let url = URL(string: urlString) else { return }
        URLSession.shared.dataTask(with: url) { [weak self] data, _, _ in
            guard let self = self, let data = data,
                  let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let cur = obj["current"] as? [String: Any],
                  let cc = (cur["cloud_cover"] as? NSNumber)?.doubleValue else { return }
            DispatchQueue.main.async {
                self.model.liveCloud = clamp(cc / 100)
                self.refresh()
            }
        }.resume()
    }
}

// MARK: - Entry point

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
