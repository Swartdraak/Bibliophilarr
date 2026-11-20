import PropTypes from 'prop-types';
import React from 'react';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { scrollDirections } from 'Helpers/Props';
import InteractiveSearchConnector from 'InteractiveSearch/InteractiveSearchConnector';
import translate from 'Utilities/String/translate';

function BookInteractiveSearchModalContent(props) {
  const {
    bookId,
    bookTitle,
    authorName,
    formatType,
    onModalClose
  } = props;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {bookId === null ?
          translate('InteractiveSearchModalHeader') :
          translate('InteractiveSearchModalHeaderBookAuthor', { bookTitle, authorName })
        }
      </ModalHeader>

      <ModalBody scrollDirection={scrollDirections.BOTH}>
        <InteractiveSearchConnector
          type="book"
          searchPayload={{
            bookId,
            ...(formatType && { formatType })
          }}
        />
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>
          {translate('Close')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

BookInteractiveSearchModalContent.propTypes = {
  bookId: PropTypes.number.isRequired,
  bookTitle: PropTypes.string.isRequired,
  authorName: PropTypes.string.isRequired,
  formatType: PropTypes.oneOf(['ebook', 'audiobook']),
  onModalClose: PropTypes.func.isRequired
};

export default BookInteractiveSearchModalContent;
