// Builds the Play Store graphics from the game captures + Meshy art.
//   swift make_store_art.swift      (run from the PlayStore folder)
// Output: feature-graphic/rilo_feature_1024x500.png, screenshots/0N_*.png (1920x1080, captioned).
// Store PNGs are written without alpha (24-bit), as Google Play requires.
import AppKit
import CoreText

let root = FileManager.default.currentDirectoryPath
let fontDir = root + "/../Client/Assets/Veil/Resources/Fonts/"
for f in ["LilitaOne-Regular.ttf", "ChakraPetch-Bold.ttf"] {
    CTFontManagerRegisterFontsForURL(URL(fileURLWithPath: fontDir + f) as CFURL, .process, nil)
}
let title = NSFont(name: "LilitaOne", size: 10) ?? NSFont.boldSystemFont(ofSize: 10)
let body = NSFont(name: "ChakraPetch-Bold", size: 10) ?? NSFont.boldSystemFont(ofSize: 10)
let purple = NSColor(calibratedRed: 0.42, green: 0.22, blue: 0.95, alpha: 1)
let gold = NSColor(calibratedRed: 1, green: 0.84, blue: 0.29, alpha: 1)

func canvas(_ w: Int, _ h: Int, _ draw: (CGContext) -> Void) -> CGImage {
    let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
    ctx.interpolationQuality = .high
    NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
    draw(ctx)
    return ctx.makeImage()!
}

func load(_ path: String) -> CGImage {
    let img = NSImage(contentsOfFile: path)!
    return img.cgImage(forProposedRect: nil, context: nil, hints: nil)!
}

/// aspect-fill `img` into the rect; bias 0..1 picks which part survives the crop (0.5 = centre)
func fill(_ ctx: CGContext, _ img: CGImage, _ r: CGRect, biasX: CGFloat = 0.5, biasY: CGFloat = 0.5) {
    let s = max(r.width / CGFloat(img.width), r.height / CGFloat(img.height))
    let w = CGFloat(img.width) * s, h = CGFloat(img.height) * s
    ctx.saveGState(); ctx.clip(to: r)
    ctx.draw(img, in: CGRect(x: r.minX - (w - r.width) * biasX, y: r.minY - (h - r.height) * biasY, width: w, height: h))
    ctx.restoreGState()
}

func text(_ s: String, _ font: NSFont, _ size: CGFloat, _ color: NSColor, at p: CGPoint, stroke: NSColor? = nil, strokeW: CGFloat = 0,
          shadow: Bool = true, italic: Bool = false) {
    var f = NSFont(descriptor: font.fontDescriptor, size: size)!
    if italic { f = NSFontManager.shared.convert(f, toHaveTrait: .italicFontMask) }
    let sh = NSShadow(); sh.shadowColor = NSColor(white: 0, alpha: 0.55); sh.shadowOffset = NSSize(width: 0, height: -size * 0.06); sh.shadowBlurRadius = size * 0.12
    if let st = stroke {
        NSAttributedString(string: s, attributes: [.font: f, .foregroundColor: color, .strokeColor: st, .strokeWidth: strokeW, .shadow: sh]).draw(at: p)
    }
    var a: [NSAttributedString.Key: Any] = [.font: f, .foregroundColor: color]
    if shadow && stroke == nil { a[.shadow] = sh }
    NSAttributedString(string: s, attributes: a).draw(at: p)
}

func gradient(_ ctx: CGContext, _ r: CGRect, _ c0: NSColor, _ c1: NSColor, horizontal: Bool) {
    let g = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: [c0.cgColor, c1.cgColor] as CFArray, locations: [0, 1])!
    ctx.saveGState(); ctx.clip(to: r)
    ctx.drawLinearGradient(g, start: CGPoint(x: r.minX, y: r.minY), end: horizontal ? CGPoint(x: r.maxX, y: r.minY) : CGPoint(x: r.minX, y: r.maxY), options: [])
    ctx.restoreGState()
}

func save(_ img: CGImage, _ path: String) {
    let rep = NSBitmapImageRep(cgImage: img)
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
    print("wrote \(path) \(img.width)x\(img.height)")
}

// ---------------------------------------------------------------- feature graphic 1024 x 500
let key = load(root + "/../Client/Assets/Veil/Resources/UI/title_keyart.png")
save(canvas(1024, 500) { ctx in
    fill(ctx, key, CGRect(x: 0, y: 0, width: 1024, height: 500), biasX: 0.5, biasY: 0.35)
    gradient(ctx, CGRect(x: 0, y: 0, width: 620, height: 500), NSColor(calibratedRed: 0.04, green: 0.02, blue: 0.14, alpha: 0.88), NSColor(calibratedRed: 0.04, green: 0.02, blue: 0.14, alpha: 0), horizontal: true)
    text("RILO", title, 150, .white, at: CGPoint(x: 48, y: 220), stroke: purple, strokeW: -14, italic: false)
    text("4 SQUADS · 4 PLAYERS EACH", body, 30, .white, at: CGPoint(x: 54, y: 170))
    text("HACK · HOLD · EXTRACT", body, 30, gold, at: CGPoint(x: 54, y: 128))
}, root + "/feature-graphic/rilo_feature_1024x500.png")

// ---------------------------------------------------------------- captioned screenshots 1920 x 1080
let shots = root + "/source/shots/", map = root + "/source/map/"
let list: [(String, String, String)] = [
    (shots + "02_lobby.png", "SQUAD UP", "4 SQUADS · 4 PLAYERS · VOICE CHAT"),
    (shots + "05_match_start.png", "DROP INTO RILO ISLAND", "FAST THIRD-PERSON SQUAD ACTION"),
    (shots + "07_competition.png", "HACK. DEFEND. OUTPLAY.", "BREACH ENEMY TERMINALS WHILE YOUR SQUAD COVERS YOU"),
    (shots + "07b_extract_final.png", "HOLD THE EXTRACTION", "FIRST SQUAD TO EXTRACT WINS"),
    (map + "map_oblique.png", "ONE ISLAND · 8 REGIONS", "CITY, SNOW, DOCKS, RUINS, BEACH, FOREST, CANYON, HYDRO"),
    (shots + "03_characters.png", "CHOOSE YOUR HERO", "5 HEROES · RIFLE, SNIPER OR FISTS"),
    (shots + "03c_store.png", "UNLOCK HEROES & SKINS", "EARN COINS EVERY MATCH"),
    (shots + "10_results.png", "BE THE MVP", "EVERY MATCH, A NEW STORY"),
]
for (i, (src, head, sub)) in list.enumerated() {
    guard FileManager.default.fileExists(atPath: src) else { print("missing \(src)"); continue }
    let img = load(src)
    let out = canvas(1920, 1080) { ctx in
        fill(ctx, img, CGRect(x: 0, y: 0, width: 1920, height: 1080))
        // caption band across the top
        gradient(ctx, CGRect(x: 0, y: 840, width: 1920, height: 240), NSColor(calibratedRed: 0.03, green: 0.02, blue: 0.12, alpha: 0), NSColor(calibratedRed: 0.03, green: 0.02, blue: 0.12, alpha: 0.92), horizontal: false)
        let hf = NSFont(descriptor: title.fontDescriptor, size: 84)!
        let w = NSAttributedString(string: head, attributes: [.font: hf]).size().width
        text(head, title, 84, .white, at: CGPoint(x: (1920 - w) / 2, y: 952), stroke: purple, strokeW: -10)
        let sf = NSFont(descriptor: body.fontDescriptor, size: 34)!
        let sw = NSAttributedString(string: sub, attributes: [.font: sf]).size().width
        text(sub, body, 34, gold, at: CGPoint(x: (1920 - sw) / 2, y: 906))
    }
    let name = String(format: "%02d_", i + 1) + head.lowercased().replacingOccurrences(of: " ", with: "_").filter { $0.isLetter || $0 == "_" } + ".png"
    save(out, root + "/screenshots/" + name)
}
