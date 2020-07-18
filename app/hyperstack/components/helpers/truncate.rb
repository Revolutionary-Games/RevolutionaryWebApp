# frozen_string_literal: true

TRUNCATE_TEXT = '...'

# Truncates a string and appends "..."
# Length should be at least 3
def truncate(str, length: 30)
  return '' if str.blank?

  if str.length <= length
    str
  else
    str[0..(length - TRUNCATE_TEXT.length)] + TRUNCATE_TEXT
  end
end
