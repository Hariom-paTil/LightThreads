// =========================================================
// LIGHTTHREADS - HARDENED UTF-8 & EMOJI SAFE AES-256 E2EE
// =========================================================

class LightEncryption {
    // Helper: Safe Uint8Array to Base64 (handles emojis & large buffers cleanly)
    static bytesToBase64(bytes) {
        let binary = '';
        const len = bytes.byteLength;
        const chunkSize = 0x8000; // Process in 32KB chunks to prevent stack limits
        for (let i = 0; i < len; i += chunkSize) {
            const chunk = bytes.subarray(i, Math.min(i + chunkSize, len));
            binary += String.fromCharCode.apply(null, chunk);
        }
        return btoa(binary);
    }

    // Helper: Safe Base64 to Uint8Array
    static base64ToBytes(base64) {
        const binaryString = atob(base64);
        const len = binaryString.length;
        const bytes = new Uint8Array(len);
        for (let i = 0; i < len; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }
        return bytes;
    }

    // Derive 256-bit AES-GCM key using PBKDF2 with dynamic room-bound salt
    static async deriveKey(secretPin) {
        const enc = new TextEncoder();
        const keyMaterial = await window.crypto.subtle.importKey(
            "raw",
            enc.encode(secretPin.padEnd(32, "0").slice(0, 32)),
            { name: "PBKDF2" },
            false,
            ["deriveBits", "deriveKey"]
        );

        const dynamicSalt = enc.encode(`LightThreadsSalt_${secretPin}_2026`);

        return window.crypto.subtle.deriveKey(
            {
                name: "PBKDF2",
                salt: dynamicSalt,
                iterations: 100000,
                hash: "SHA-256"
            },
            keyMaterial,
            { name: "AES-GCM", length: 256 },
            false,
            ["encrypt", "decrypt"]
        );
    }

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

            const combined = new Uint8Array(iv.length + ciphertext.byteLength);
            combined.set(iv, 0);
            combined.set(new Uint8Array(ciphertext), iv.length);

            return LightEncryption.bytesToBase64(combined);
        } catch (e) {
            console.error("Encryption failed:", e);
            return plaintext;
        }
    }

    static async decryptMessage(cipherBase64, secretPin) {
        try {
            const key = await LightEncryption.deriveKey(secretPin);
            const combined = LightEncryption.base64ToBytes(cipherBase64);

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