package com.litertlm.dotnet

import com.google.ai.edge.litertlm.Engine
import com.google.ai.edge.litertlm.Conversation
import com.google.ai.edge.litertlm.Contents
import com.google.ai.edge.litertlm.ConversationConfig
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.collect

/**
 * Minimalist bridge that handles ONLY the Kotlin-specific parts
 * that .NET Android binding generator can't handle:
 *   1. suspend fun initialize()
 *   2. suspend fun sendMessage()
 *   3. Flow<String> streaming
 *   4. Contents.Companion.of() factory
 *   5. ConversationConfig.Builder (inner builder pattern)
 */
object LiteRtLmInterop {

    // ── Callbacks (implemented in C#) ────────────────

    interface InitCallback {
        fun onSuccess()
        fun onError(message: String)
    }

    interface MessageCallback {
        fun onSuccess(text: String)
        fun onError(message: String)
    }

    interface StreamCallback {
        fun onToken(token: String)
        fun onComplete()
        fun onError(message: String)
    }

    // ── Bridge methods ───────────────────────────────

    @JvmStatic
    fun initializeAsync(engine: Engine, callback: InitCallback) {
        CoroutineScope(Dispatchers.IO).launch {
            try {
                engine.initialize()
                withContext(Dispatchers.Main) { callback.onSuccess() }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) { callback.onError(e.message ?: "Unknown error") }
            }
        }
    }

    @JvmStatic
    fun sendMessageAsync(conversation: Conversation, text: String, callback: MessageCallback) {
        CoroutineScope(Dispatchers.IO).launch {
            try {
                val contents = Contents.of(text)
                val response = conversation.sendMessage(contents)
                withContext(Dispatchers.Main) { callback.onSuccess(response) }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) { callback.onError(e.message ?: "Unknown error") }
            }
        }
    }

    @JvmStatic
    fun sendMessageStreamingAsync(conversation: Conversation, text: String, callback: StreamCallback) {
        CoroutineScope(Dispatchers.IO).launch {
            try {
                val contents = Contents.of(text)
                conversation.sendMessageStreaming(contents).collect { token ->
                    withContext(Dispatchers.Main) { callback.onToken(token) }
                }
                withContext(Dispatchers.Main) { callback.onComplete() }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) { callback.onError(e.message ?: "Unknown error") }
            }
        }
    }

    @JvmStatic
    fun createConversationConfig(
        systemInstruction: String?,
        topK: Int,
        topP: Float,
        temperature: Float
    ): ConversationConfig {
        val builder = ConversationConfig.Builder()
        if (!systemInstruction.isNullOrEmpty()) {
            builder.setSystemInstruction(Contents.of(systemInstruction))
        }
        builder.setTopK(topK)
        builder.setTopP(topP)
        builder.setTemperature(temperature)
        return builder.build()
    }
}
