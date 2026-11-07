import React from 'react';

interface PageHeaderProps {
  title: string;
  // The one-line description. It is shown as a tooltip on the info icon, not as a line under the title.
  info?: string;
  // Right side of the row: the report date controls, then the page's own buttons.
  actions?: React.ReactNode;
}

// One compact row for the top of a page: the title on the left, the report controls and buttons on the right.
export const PageHeader: React.FC<PageHeaderProps> = ({ title, info, actions }) => (
  <div className="page-header">
    <div className="page-header__title">
      <h1 className="view-title">{title}</h1>
      {info && (
        <span className="page-header__info" title={info} aria-label={info} role="img" tabIndex={0}>
          ⓘ
        </span>
      )}
    </div>
    {actions && <div className="page-header__actions">{actions}</div>}
  </div>
);
