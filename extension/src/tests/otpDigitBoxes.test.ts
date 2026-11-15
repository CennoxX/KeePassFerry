// @vitest-environment jsdom
import { beforeAll, describe, expect, it, vi } from "vitest";
import { findOtpTarget } from "../page/OtpFields";

/*
Ziffernkästchen (je Ziffer ein Feld): eine Reihe aufeinanderfolgender Kästchen gilt als Codeziel, egal wie tief jedes
Kästchen verschachtelt ist (MUI bei Autodesk, #48).
*/

beforeAll(() => {
    // jsdom berechnet kein Layout; die Erkennung zählt nur sichtbare Felder auf der Seite
    Object.defineProperty(HTMLElement.prototype, "offsetWidth", { configurable: true, get: () => 32 });
    Object.defineProperty(HTMLElement.prototype, "offsetHeight", { configurable: true, get: () => 32 });
    HTMLElement.prototype.getBoundingClientRect = () => ({ left: 10, top: 10, right: 42, bottom: 42, width: 32, height: 32, x: 10, y: 10, toJSON: () => ({}) });
});

let clock = Date.now();

function page(title: string, html: string): string[] {
    // Zwei-Faktor-Kontext wird kurz zwischengespeichert: Uhr je Fall weiterdrehen
    clock += 5000;
    vi.spyOn(Date, "now").mockReturnValue(clock);
    document.title = title;
    document.body.innerHTML = html;
    const names = (findOtpTarget() ?? []).map(el => el.name);
    vi.restoreAllMocks();
    return names;
}

const box = (n: number, wrap = 0) => {
    let html = `<input type="text" name="d${n}" maxlength="1" inputmode="numeric">`;
    for (let i = 0; i < wrap; i++) html = `<div>${html}</div>`;
    return html;
};
const boxes = (count: number, wrap = 0) => Array.from({ length: count }, (_, i) => box(i + 1, wrap)).join("");
const six = ["d1", "d2", "d3", "d4", "d5", "d6"];

describe("Ziffernkästchen", () => {
    it("flach nebeneinander", () => {
        expect(page("Two-factor authentication", `<form>${boxes(6)}</form>`)).toEqual(six);
    });

    it("jedes Kästchen in eigenen Hüllen (MUI)", () => {
        expect(page("Two-factor authentication", `<form><div>${boxes(6, 3)}</div></form>`)).toEqual(six);
    });

    it("durch ein anderes Feld getrennt: keine Reihe", () => {
        expect(page("Two-factor authentication", `<form>${boxes(3)}<input type="email" name="other">${boxes(3)}</form>`)).toEqual([]);
    });

    it("Code per SMS: nicht ausfüllen", () => {
        const html = `<form><label>Enter the code we sent by SMS</label>${boxes(6)}</form>`.replace(/name="d/g, 'aria-label="SMS digit" name="d');
        expect(page("Verify your phone", html)).toEqual([]);
    });
});
