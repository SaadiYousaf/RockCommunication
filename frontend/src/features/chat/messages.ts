/**
 * Centralized user-facing copy for the Chat feature.
 * Keep inline message strings out of the page — reference these instead.
 */
export const CHAT_MSG = {
  // Search placeholders (no user-facing copy inline).
  searchConversations: "Search conversations...",
  searchInConversation: "Search in conversation…",
  searchPeople: "Search people…",
  // Message actions
  editFailed: "Couldn't edit",
  deleteMessageTitle: "Delete message?",
  deleteMessageDesc: "This removes the message for everyone.",
  deleteFailed: "Couldn't delete",
  sendFailed: "Couldn't send message",
  fileTooLargeTitle: "File too large",
  fileTooLargeDesc: (maxMb: number) => `Max ${maxMb} MB.`,
  uploadFailed: "Upload failed",

  // Rooms / direct messages
  roomCreated: "Room created",
  createRoomFailed: "Couldn't create room",
  startChatFailed: "Couldn't start chat",

  // Empty states
  noConversationsTitle: "No conversations",
  noConversationsDesc: "Start a new room to chat with your team.",
  pickConversationTitle: "Pick a conversation",
  pickConversationDesc: "Select a room from the left to start chatting, or create a new one.",
  noMessagesTitle: "No messages yet",
  noMessagesDesc: "Be the first to break the ice.",
} as const;
