// =========================================================
// LIGHTTHREADS - CLIENT-SIDE AES-GCM 256-BIT ENCRYPTION
// =========================================================

class LightEncryption {
    // Generate an ephemeral 256-bit AES-GCM key from session PIN/Token
    static async deriveKey(secretPin) {
        const enc = new TextEncoder();
        const keyMaterial = await window.crypto.subtle.importKey(
            "raw",
            enc.encode(secretPin.padEnd(32, "0").slice(0, 32)),
            { name: "PBKDF2" },
            false,
            ["deriveBits", "deriveKey"]
        );

        return window.crypto.subtle.deriveKey(
            {
                name: "PBKDF2",
                salt: enc.encode("LightThreadsSalt2026"),
                iterations: 100000,
                hash: "SHA-256"
            },
            keyMaterial,
            { name: "AES-GCM", length: 256 },
            false,
            ["encrypt", "decrypt"]
        );
    }

    // Encrypt plaintext string into base64 payload
    static async encryptMessage(plaintext, secretPin) {
        try {
            const key = await LightEncryption.deriveKey(secretPin);
            const iv = window.crypto.getRandomValues(new Uint8Array(12)); // 96-bit IV
            const enc = new TextEncoder();

            const ciphertext = await window.crypto.subtle.encrypt(
                { name: "AES-GCM", iv: iv },
                key,
                enc.encode(plaintext)
            );

            // Combine IV and Ciphertext for transport
            const combined = new Uint8Array(iv.length + ciphertext.byteLength);
            combined.set(iv, 0);
            combined.set(new Uint8Array(ciphertext), iv.length);

            return btoa(String.fromCharCode.apply(null, combined));
        } catch (e) {
            console.error("Encryption failed:", e);
            return plaintext; // Fallback
        }
    }

    // Decrypt base64 payload into plaintext string
    static async decryptMessage(cipherBase64, secretPin) {
        try {
            const key = await LightEncryption.deriveKey(secretPin);
            const combined = new Uint8Array(
                atob(cipherBase64).split("").map(c => c.charCodeAt(0))
            );

            const iv = combined.slice(0, 12);
            const ciphertext = combined.slice(12);

            const decrypted = await window.crypto.subtle.decrypt(
                { name: "AES-GCM", iv: iv },
                key,
                ciphertext
            );

            return new TextDecoder().decode(decrypted);
        } catch (e) {
            console.error("Decryption failed:", e);
            return "[ENCRYPTED MESSAGE - DECRYPTION FAILED]";
        }
    }
}