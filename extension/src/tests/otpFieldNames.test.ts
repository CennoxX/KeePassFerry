// @vitest-environment jsdom
import { describe, expect, it, vi } from "vitest";
import { isOtpField } from "../page/OtpFields";

/*
Feldnamen echter Zwei-Faktor-Seiten (Liste aus PR #49): eindeutige Namen gelten immer als Codefeld, allgemeine
("code", "pin", "token") nur mit Zwei-Faktor-Text auf der Seite. Wörter in anderen Wörtern zählen nicht.
*/

let clock = Date.now();

function withPage(title: string, names: string[]): string[] {
    // Zwei-Faktor-Kontext wird kurz zwischengespeichert: Uhr je Aufruf weiterdrehen
    clock += 5000;
    vi.spyOn(Date, "now").mockReturnValue(clock);
    document.title = title;
    document.body.innerHTML = "";
    const found = names.filter(name => {
        const el = document.createElement("input");
        el.setAttribute("type", "text");
        el.setAttribute("name", name);
        document.body.appendChild(el);
        return isOtpField(el);
    });
    vi.restoreAllMocks();
    return found;
}

describe("Feldnamen für Einmalcodes", () => {
    const strong = ["app_otp", "otp_attempt", "user_otp_attempt", "OATHToken", "wpOATHToken", "mfa2", "2fa_code"];
    const weak = ["pin1", "pin_box_0", "approvals_code", "challenge_code", "code_text_input_0"];
    const never = ["zipcode", "postcode", "pincode", "otpauth_uri", "spinner", "pinterest", "shipping"];

    it("eindeutige Namen auch ohne Zwei-Faktor-Text", () => {
        expect(withPage("Sign in", [...strong, ...weak, ...never])).toEqual(strong);
    });

    it("allgemeine Namen nur mit Zwei-Faktor-Text", () => {
        expect(withPage("Two-factor authentication", [...strong, ...weak, ...never])).toEqual([...strong, ...weak]);
    });
});
