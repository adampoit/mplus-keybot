import { cx } from "../css";
import styles from "./ui.module.css";

export function Alert({
  kind,
  title,
  message,
}: {
  kind: "error" | "success";
  title: string;
  message: string;
}) {
  return (
    <div className={cx(styles.alert, styles[kind])}>
      <strong>{title}</strong>
      <br />
      {message}
    </div>
  );
}

export function EmptyState({
  icon,
  message,
}: {
  icon?: string;
  message: string;
}) {
  return (
    <div className={styles.emptyState}>
      {icon ? <div className={styles.emptyStateIcon}>{icon}</div> : null}
      <p>{message}</p>
    </div>
  );
}

export function Loading() {
  return <div className={styles.emptyState}>Loading…</div>;
}
