import {
  // Navigation / structure
  LayoutDashboard, Users, Phone, Briefcase, Search, Bell, Settings, MessageSquare,
  BarChart3, ShieldCheck, Target, Flag, Calendar, ListChecks, Plus, LogOut, Check, X,
  ArrowRight, Menu, Inbox, Star, Building2, FileText, Filter, Clock, UserPlus,
  // Polish / actions
  ChevronRight, ChevronLeft, ChevronDown, ChevronUp, MoreHorizontal, MoreVertical,
  Pencil, Trash2, Copy, Download, Upload, ExternalLink, Eye, EyeOff, Lock, Unlock,
  RefreshCw, Loader2, AlertTriangle, AlertCircle, Info, CheckCircle2, XCircle,
  Mail, Send, Paperclip, Archive, Pin, Tag, Bookmark, Sparkles, Zap, TrendingUp,
  TrendingDown, DollarSign, CreditCard, Headphones, PhoneCall, PhoneIncoming,
  PhoneOutgoing, PhoneOff, Mic, MicOff, Volume2, VolumeX, Play, Pause, SkipForward,
  SkipBack, Save, Share2, Globe, MapPin, Home, User, UserCheck, UserCog, UserX,
  Shield, Key, Database, Server, Cpu, Activity, FileSpreadsheet, FilePlus,
  FolderKanban, Workflow, GitBranch, Layers, Grid3x3, List as ListIcon, Columns3,
  Smile, File as FileIcon,
} from "lucide-react";
import type { ComponentType, SVGProps } from "react";
import { cn } from "./cn";

type LucideIcon = ComponentType<SVGProps<SVGSVGElement>>;

/**
 * Stable name → lucide component map. Keeps the existing `<Icon name="..." />` API
 * so we don't have to touch ~200 call sites across the app, while delivering a
 * crisp, industry-standard icon set (lucide-react, used by Vercel/Linear/Shadcn).
 *
 * The first block preserves every legacy name. The second block adds richer icons
 * pages can opt into for enterprise polish (chevrons, status, finance, comms, etc.).
 */
const icons = {
  // ── Legacy names (do not rename) ──────────────────────────────────────────
  dashboard:    LayoutDashboard,
  users:        Users,
  phone:        Phone,
  briefcase:    Briefcase,
  search:       Search,
  bell:         Bell,
  cog:          Settings,
  chat:         MessageSquare,
  chart:        BarChart3,
  shield:       ShieldCheck,
  target:       Target,
  flag:         Flag,
  calendar:     Calendar,
  list:         ListChecks,
  plus:         Plus,
  logout:       LogOut,
  check:        Check,
  x:            X,
  arrowRight:   ArrowRight,
  menu:         Menu,
  inbox:        Inbox,
  star:         Star,
  building:     Building2,
  doc:          FileText,
  filter:       Filter,
  clock:        Clock,
  userPlus:     UserPlus,

  // ── Extended set ──────────────────────────────────────────────────────────
  chevronRight: ChevronRight,
  chevronLeft:  ChevronLeft,
  chevronDown:  ChevronDown,
  chevronUp:    ChevronUp,
  moreH:        MoreHorizontal,
  moreV:        MoreVertical,
  edit:         Pencil,
  trash:        Trash2,
  copy:         Copy,
  download:     Download,
  upload:       Upload,
  externalLink: ExternalLink,
  eye:          Eye,
  eyeOff:       EyeOff,
  lock:         Lock,
  unlock:       Unlock,
  refresh:      RefreshCw,
  spinner:      Loader2,
  alert:        AlertTriangle,
  warning:      AlertCircle,
  info:         Info,
  success:      CheckCircle2,
  error:        XCircle,
  mail:         Mail,
  send:         Send,
  attach:       Paperclip,
  archive:      Archive,
  pin:          Pin,
  tag:          Tag,
  bookmark:     Bookmark,
  sparkles:     Sparkles,
  zap:          Zap,
  trendUp:      TrendingUp,
  trendDown:    TrendingDown,
  dollar:       DollarSign,
  card:         CreditCard,
  headset:      Headphones,
  phoneCall:    PhoneCall,
  phoneIn:      PhoneIncoming,
  phoneOut:     PhoneOutgoing,
  phoneOff:     PhoneOff,
  mic:          Mic,
  micOff:       MicOff,
  volume:       Volume2,
  mute:         VolumeX,
  play:         Play,
  pause:        Pause,
  skipFwd:      SkipForward,
  skipBack:     SkipBack,
  save:         Save,
  share:        Share2,
  globe:        Globe,
  mapPin:       MapPin,
  home:         Home,
  user:         User,
  userCheck:    UserCheck,
  userCog:      UserCog,
  userX:        UserX,
  shieldAlt:    Shield,
  key:          Key,
  database:     Database,
  server:       Server,
  cpu:          Cpu,
  activity:     Activity,
  spreadsheet:  FileSpreadsheet,
  filePlus:     FilePlus,
  kanban:       FolderKanban,
  workflow:     Workflow,
  branch:       GitBranch,
  layers:       Layers,
  grid:         Grid3x3,
  rows:         ListIcon,
  cols:         Columns3,
  smile:        Smile,
  file:         FileIcon,
} as const satisfies Record<string, LucideIcon>;

export type IconName = keyof typeof icons;

export function Icon({
  name, size = 18, className, strokeWidth = 1.75,
}: {
  name: IconName;
  size?: number;
  className?: string;
  strokeWidth?: number;
}) {
  const Cmp = icons[name];
  return (
    <Cmp
      width={size}
      height={size}
      strokeWidth={strokeWidth}
      className={cn("shrink-0", className)}
      aria-hidden="true"
    />
  );
}
