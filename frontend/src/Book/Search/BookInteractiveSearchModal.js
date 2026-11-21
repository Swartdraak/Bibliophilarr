import PropTypes from 'prop-types';
import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import BookInteractiveSearchModalContent from './BookInteractiveSearchModalContent';

function BookInteractiveSearchModal(props) {
  const {
    isOpen,
    bookId,
    bookTitle,
    authorName,
    formatType,
    onModalClose
  } = props;

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.EXTRA_EXTRA_LARGE}
      closeOnBackgroundClick={false}
      onModalClose={onModalClose}
    >
      <BookInteractiveSearchModalContent
        bookId={bookId}
        bookTitle={bookTitle}
        authorName={authorName}
        formatType={formatType}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

BookInteractiveSearchModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  bookId: PropTypes.number.isRequired,
  bookTitle: PropTypes.string.isRequired,
  authorName: PropTypes.string.isRequired,
  formatType: PropTypes.oneOf(['ebook', 'audiobook']),
  onModalClose: PropTypes.func.isRequired
};

export default BookInteractiveSearchModal;
