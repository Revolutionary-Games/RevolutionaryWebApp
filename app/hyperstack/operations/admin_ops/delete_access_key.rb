# frozen_string_literal: true

module AdminOps
  # Invalidate an access key
  class DeleteAccessKey < Hyperstack::ServerOp
    param :acting_user
    param :key_id
    validate { params.acting_user.admin? }
    add_error(:key_id, :does_not_exist, 'key does not exist') {
      !(@key = AccessKey.find_by_id(params.key_id))
    }
    step {
      @key.destroy!
    }
  end
end
