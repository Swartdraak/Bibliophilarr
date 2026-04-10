import PropTypes from 'prop-types';
import React from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import styles from './Wiki.css';

function WikiPage({ page }) {
  const iconName = icons[page.icon] || icons.INFO;

  return (
    <div>
      <div className={styles.breadcrumb}>
        <Link
          className={styles.breadcrumbLink}
          to={getPathWithUrlBase('/system/wiki')}
        >
          Wiki
        </Link>
        <span className={styles.breadcrumbSeparator}>›</span>
        <span>{page.title}</span>
      </div>

      <div className={styles.section}>
        <h2 className={styles.sectionTitle}>
          <Icon
            name={iconName}
            size={18}
          />
          {' '}{page.title}
        </h2>

        {page.description &&
          <p className={styles.paragraph}>
            {page.description}
          </p>
        }
      </div>

      {
        page.content.map((section, index) => (
          <div key={index} className={styles.section}>
            {section.heading &&
              <h3 className={styles.sectionTitle}>
                {section.heading}
              </h3>
            }

            {section.paragraphs && section.paragraphs.map((para, pIdx) => (
              <p key={pIdx} className={styles.paragraph}>
                {para}
              </p>
            ))}

            {section.list &&
              <ul className={styles.list}>
                {section.list.map((item, lIdx) => (
                  <li key={lIdx} className={styles.listItem}>
                    {item}
                  </li>
                ))}
              </ul>
            }

            {section.note &&
              <div className={styles.noteBox}>
                <strong>Note: </strong>{section.note}
              </div>
            }

            {section.warning &&
              <div className={styles.warningBox}>
                <strong>Important: </strong>{section.warning}
              </div>
            }
          </div>
        ))
      }
    </div>
  );
}

WikiPage.propTypes = {
  page: PropTypes.object.isRequired
};

export default WikiPage;
