package com.veilstudio.rilo;

import android.app.Activity;
import android.os.CancellationSignal;

import androidx.credentials.ClearCredentialStateRequest;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.ClearCredentialException;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import java.util.concurrent.Executor;
import java.util.concurrent.Executors;

/**
 * "Sign in with Google" for Rilo via Android Credential Manager. Unity calls signIn() and gets one
 * Listener.onResult callback (on a background thread) with the Google ID token for the server to verify.
 */
public final class RiloGoogleSignIn {
    public interface Listener {
        void onResult(boolean ok, String idToken, String email, String name, String error);
    }

    private static final Executor EXEC = Executors.newSingleThreadExecutor();

    public static void signIn(final Activity activity, final String serverClientId, final Listener listener) {
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(serverClientId).build();
                    GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
                    CredentialManager.create(activity).getCredentialAsync(activity, request, new CancellationSignal(), EXEC,
                        new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                            @Override public void onResult(GetCredentialResponse response) {
                                Credential c = response.getCredential();
                                if (c instanceof CustomCredential && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(c.getType())) {
                                    try {
                                        GoogleIdTokenCredential g = GoogleIdTokenCredential.createFrom(((CustomCredential) c).getData());
                                        String name = g.getDisplayName() != null ? g.getDisplayName() : "";
                                        listener.onResult(true, g.getIdToken(), g.getId(), name, "");
                                    } catch (Exception e) {
                                        listener.onResult(false, "", "", "", "bad Google credential: " + e.getMessage());
                                    }
                                } else {
                                    listener.onResult(false, "", "", "", "unexpected credential " + c.getType());
                                }
                            }

                            @Override public void onError(GetCredentialException e) {
                                boolean cancelled = e instanceof GetCredentialCancellationException;
                                listener.onResult(false, "", "", "", cancelled ? "cancelled" : e.getClass().getSimpleName() + ": " + e.getMessage());
                            }
                        });
                } catch (Throwable t) {
                    listener.onResult(false, "", "", "", t.toString());
                }
            }
        });
    }

    /** Forget the chosen account so the picker shows again next time. */
    public static void signOut(final Activity activity) {
        try {
            CredentialManager.create(activity).clearCredentialStateAsync(new ClearCredentialStateRequest(), new CancellationSignal(), EXEC,
                new CredentialManagerCallback<Void, ClearCredentialException>() {
                    @Override public void onResult(Void v) { }
                    @Override public void onError(ClearCredentialException e) { }
                });
        } catch (Throwable ignored) { }
    }
}
